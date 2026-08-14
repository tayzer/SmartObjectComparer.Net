using System.Collections;
using System.Collections.Concurrent;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Comparers;

public sealed class CompareNetObjectsResponseComparer : IResponseComparer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    // Rule patterns repeat across every difference in a response; compile once per
    // distinct rule instead of rebuilding the pattern string and regex on each check.
    private static readonly ConcurrentDictionary<string, Regex> PathRulePatternCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Regex> NameRulePatternCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, Regex> WildcardRulePatternCache = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<ComparisonOptions, ThreadLocal<CompareLogic>> CompareLogicCaches = new();
    private static readonly ConditionalWeakTable<ComparisonOptions, PreparedComparisonRules> PreparedRuleCaches = new();
    private static readonly ConcurrentDictionary<Type, bool> AcyclicSortTypeCache = new();
    private static readonly BoundedByteArrayPool SortBufferPool = new();
    private readonly IRunArtifactStore artifactStore;
    private readonly IResponseBodyDeserializer deserializer;
    private readonly bool useLegacyNormalizer;
    // CompareLogic owns reflection/type metadata caches. Keep one instance per
    // worker thread for the current options reference; constructing it per pair
    // defeats Caching=true and adds avoidable high-volume allocation.
    private readonly ThreadLocal<CompareLogicState?> compareLogicCache = new();

    public CompareNetObjectsResponseComparer(
        IRunArtifactStore artifactStore,
        IResponseBodyDeserializer deserializer)
        : this(artifactStore, deserializer, useLegacyNormalizer: false)
    {
    }

    internal CompareNetObjectsResponseComparer(
        IRunArtifactStore artifactStore,
        IResponseBodyDeserializer deserializer,
        bool useLegacyNormalizer)
    {
        this.artifactStore = artifactStore;
        this.deserializer = deserializer;
        this.useLegacyNormalizer = useLegacyNormalizer;
    }

    public async Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        CancellationToken cancellationToken = default)
        => await CompareAsync(request, options, responseA, responseB, errorMessage, null, cancellationToken).ConfigureAwait(false);

    internal async Task<RequestPairResult> CompareAsync(
        RequestItem request,
        RunOptions options,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage,
        DetailedCompareMetricsCollector? timing,
        CancellationToken cancellationToken = default)
    {
        if (!CanModelCompare(responseA, responseB, errorMessage))
        {
            return RequestPairResult.Classify(request, responseA, responseB, errorMessage);
        }

        if (CanUseHashFastPath(responseA!, responseB!, options.Comparison))
        {
            return RequestPairResult.Classify(request, responseA, responseB);
        }

        try
        {
            ResponseArtifactMetadata leftResponse = responseA!;
            ResponseArtifactMetadata rightResponse = responseB!;
            Stream openedA = await TimeAsync(timing, static (c, e) => c.AddArtifactOpen(e), () => artifactStore.OpenReadAsync(leftResponse.Artifact, cancellationToken)).ConfigureAwait(false);
            await using Stream bodyA = timing is null ? openedA : new CountingReadStream(openedA, timing.AddArtifactBytesRead);
            Stream openedB = await TimeAsync(timing, static (c, e) => c.AddArtifactOpen(e), () => artifactStore.OpenReadAsync(rightResponse.Artifact, cancellationToken)).ConfigureAwait(false);
            await using Stream bodyB = timing is null ? openedB : new CountingReadStream(openedB, timing.AddArtifactBytesRead);

            object modelA = await TimeAsync(timing, static (c, e) => c.AddDeserialization(e), () => deserializer.DeserializeAsync(options.ResponseModelName, bodyA, leftResponse.ContentType, options.Comparison, cancellationToken)).ConfigureAwait(false);
            object modelB = await TimeAsync(timing, static (c, e) => c.AddDeserialization(e), () => deserializer.DeserializeAsync(options.ResponseModelName, bodyB, rightResponse.ContentType, options.Comparison, cancellationToken)).ConfigureAwait(false);

            IReadOnlyList<ComparisonDifference> differences = useLegacyNormalizer
                ? CompareModelsLegacy(modelA, modelB, options.Comparison, GetCompareLogic(options.Comparison), timing)
                : CompareModels(modelA, modelB, options.Comparison, GetCompareLogic(options.Comparison), timing);

            return RequestPairResult.FromComparison(
                request,
                leftResponse,
                rightResponse,
                differences,
                differences.Count == 0 ? "Responses are equal after configured comparison rules." : null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                responseA,
                responseB,
                ex.Message);
        }
    }

    /// <summary>
    /// Compares two already-materialized models. The pipeline's comparison phase
    /// works on objects the mapping phase produced rather than on persisted
    /// artifacts, so the rule application lives here and both callers share it.
    /// </summary>
    public static IReadOnlyList<ComparisonDifference> CompareModels(
        object modelA,
        object modelB,
        ComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(modelA);
        ArgumentNullException.ThrowIfNull(modelB);
        ArgumentNullException.ThrowIfNull(options);

        return CompareModels(modelA, modelB, options, GetSharedCompareLogic(options), null);
    }

    internal static IReadOnlyList<ComparisonDifference> CompareModels(
        object modelA,
        object modelB,
        ComparisonOptions options,
        DetailedCompareMetricsCollector? timing)
    {
        ArgumentNullException.ThrowIfNull(modelA);
        ArgumentNullException.ThrowIfNull(modelB);
        ArgumentNullException.ThrowIfNull(options);
        return CompareModels(modelA, modelB, options, GetSharedCompareLogic(options), timing);
    }

    private static IReadOnlyList<ComparisonDifference> CompareModels(
        object modelA,
        object modelB,
        ComparisonOptions options,
        CompareLogic compareLogic,
        DetailedCompareMetricsCollector? timing)
    {
        PreparedComparisonRules rules = GetPreparedRules(options);
        ComparisonModelPreparation prepared = Time(
            timing,
            static (c, e) => c.AddNormalization(e),
            () => ComparisonModelPreparation.Create(modelA, modelB, options, rules, timing));
        try
        {
            ComparisonResult comparisonResult = Time(timing, static (c, e) => c.AddCompareNetObjects(e), () => compareLogic.Compare(prepared.ModelA, prepared.ModelB));
            return Time(timing, static (c, e) => c.AddMaterialization(e), () => MaterializeDifferences(comparisonResult.Differences, options));
        }
        finally
        {
            Time(timing, static (c, e) => c.AddNormalization(e), prepared.Dispose);
        }
    }

    private static IReadOnlyList<ComparisonDifference> CompareModelsLegacy(
        object modelA,
        object modelB,
        ComparisonOptions options,
        CompareLogic compareLogic,
        DetailedCompareMetricsCollector? timing)
    {
        (object modelA, object modelB) normalized = Time(
            timing,
            static (c, e) => c.AddNormalization(e),
            () => (LegacyComparisonModelNormalizer.Normalize(modelA, options), LegacyComparisonModelNormalizer.Normalize(modelB, options)));
        ComparisonResult comparisonResult = Time(timing, static (c, e) => c.AddCompareNetObjects(e), () => compareLogic.Compare(normalized.modelA, normalized.modelB));
        return Time(timing, static (c, e) => c.AddMaterialization(e), () => MaterializeDifferences(comparisonResult.Differences, options));
    }

    private static PreparedComparisonRules GetPreparedRules(ComparisonOptions options) =>
        PreparedRuleCaches.GetValue(options, static value => new PreparedComparisonRules(value));

    private static T Time<T>(DetailedCompareMetricsCollector? timing, Action<DetailedCompareMetricsCollector, TimeSpan> record, Func<T> action)
    {
        if (timing is null) { return action(); }
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            stopwatch.Stop();
            record(timing, stopwatch.Elapsed);
        }
    }

    private static void Time(DetailedCompareMetricsCollector? timing, Action<DetailedCompareMetricsCollector, TimeSpan> record, Action action)
    {
        if (timing is null)
        {
            action();
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            record(timing, stopwatch.Elapsed);
        }
    }

    private static async Task<T> TimeAsync<T>(DetailedCompareMetricsCollector? timing, Action<DetailedCompareMetricsCollector, TimeSpan> record, Func<Task<T>> action)
    {
        if (timing is null) { return await action().ConfigureAwait(false); }
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            return await action().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            record(timing, stopwatch.Elapsed);
        }
    }

    private CompareLogic GetCompareLogic(ComparisonOptions options)
    {
        CompareLogicState? state = compareLogicCache.Value;
        if (state is null || !ReferenceEquals(state.Options, options))
        {
            state = new CompareLogicState(options, CreateCompareLogic(options));
            compareLogicCache.Value = state;
        }

        return state.CompareLogic;
    }

    private sealed record CompareLogicState(ComparisonOptions Options, CompareLogic CompareLogic);

    private static CompareLogic GetSharedCompareLogic(ComparisonOptions options) =>
        CompareLogicCaches.GetValue(
            options,
            static value => new ThreadLocal<CompareLogic>(() => CreateCompareLogic(value))).Value!;

    private static CompareLogic CreateCompareLogic(ComparisonOptions options)
    {
        CompareLogic compareLogic = new CompareLogic();
        compareLogic.Config.MaxDifferences = options.IncludeAllDifferences ? int.MaxValue : CalculateInternalMaxDifferences(options);
        compareLogic.Config.IgnoreObjectTypes = false;
        compareLogic.Config.ComparePrivateFields = false;
        compareLogic.Config.ComparePrivateProperties = true;
        compareLogic.Config.CompareReadOnly = true;
        compareLogic.Config.IgnoreCollectionOrder = false;
        compareLogic.Config.CaseSensitive = !options.IgnoreStringCase;
        compareLogic.Config.Caching = true;
        compareLogic.Config.SkipInvalidIndexers = true;
        compareLogic.Config.MembersToIgnore = BuildMembersToIgnore();
        compareLogic.Config.AttributesToIgnore = new List<Type> { typeof(JsonIgnoreAttribute) };

        return compareLogic;
    }

    private static List<string> BuildMembersToIgnore() =>
        new List<string>
        {
            "Length",
            "LongLength",
            "NativeLength",
        };

    private static int CalculateInternalMaxDifferences(ComparisonOptions options)
    {
        int requested = Math.Max(1, options.MaxDifferences);
        // Post-processing can suppress entries (filters and deduplication), so gather headroom
        // before applying the user-facing max in this comparer.
        int ruleHeadroom = Math.Max(100, options.IgnoreRules.Count * 25);
        int expanded = Math.Max(requested * 10, requested + ruleHeadroom);
        return Math.Min(expanded, 10000);
    }

    private static bool CanModelCompare(
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage) =>
        string.IsNullOrWhiteSpace(errorMessage)
        && responseA is not null
        && responseB is not null
        && IsSuccessStatusCode(responseA.StatusCode)
        && IsSuccessStatusCode(responseB.StatusCode);

    private static bool CanUseHashFastPath(
        ResponseArtifactMetadata responseA,
        ResponseArtifactMetadata responseB,
        ComparisonOptions options) =>
        !options.HasComparisonAffectingOptions
        && responseA.ContentLength == responseB.ContentLength
        && string.Equals(responseA.Sha256, responseB.Sha256, StringComparison.OrdinalIgnoreCase);

    private static bool ShouldIgnoreCollectionOrder(ComparisonOptions options) =>
        options.IgnoreCollectionOrder
        || options.IgnoreRules.Any(rule => rule.IgnoreCollectionOrder)
        || options.SmartIgnoreRules.Any(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.CollectionOrdering);

    private static bool ShouldFilterDifference(Difference difference, string normalizedPath, ComparisonOptions options, PreparedComparisonRules rules) =>
        rules.ShouldIgnorePath(normalizedPath)
        || rules.ShouldIgnoreSmartDifference(difference, normalizedPath)
        || ShouldIgnoreByTrailingWhitespace(difference, options)
        || ShouldIgnoreByNullEmptyCollectionRule(difference, normalizedPath, options, rules);

    private static bool ShouldIgnoreByRule(string propertyPath, ComparisonOptions options) =>
        options.IgnoreRules
            .Where(rule => rule.IgnoreCompletely)
            .Any(rule => PathMatches(rule.PropertyPath, propertyPath));

    private static bool ShouldIgnoreByTrailingWhitespace(Difference difference, ComparisonOptions options)
    {
        if (!options.IgnoreTrailingWhitespaceAtEnd)
        {
            return false;
        }

        string? valueA = difference.Object1Value?.ToString();
        string? valueB = difference.Object2Value?.ToString();

        return valueA is not null
            && valueB is not null
            && string.Equals(valueA.TrimEnd(), valueB.TrimEnd(), StringComparison.Ordinal);
    }

    private static bool ShouldIgnoreByNullEmptyCollectionRule(
        Difference difference,
        string normalizedPath,
        ComparisonOptions options,
        PreparedComparisonRules rules)
    {
        bool appliesGlobally = options.TreatNullAndEmptyCollectionsAsEqual;
        bool appliesByRule = rules.ShouldTreatNullAndEmptyAsEqual(normalizedPath);

        if (!appliesGlobally && !appliesByRule)
        {
            return false;
        }

        return (IsMissingOrNullValue(difference.Object1Value) && IsEmptyCollectionValue(difference.Object2Value))
            || (IsMissingOrNullValue(difference.Object2Value) && IsEmptyCollectionValue(difference.Object1Value));
    }

    private static bool IsMissingOrNullValue(object? value)
    {
        if (value is null)
        {
            return true;
        }

        string text = value.ToString() ?? string.Empty;
        return string.Equals(text, "(null)", StringComparison.OrdinalIgnoreCase)
            || string.Equals(text, "null", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsEmptyCollectionValue(object? value)
    {
        if (value is null)
        {
            return false;
        }

        if (value is ICollection collection)
        {
            return collection.Count == 0;
        }

        if (value is IEnumerable enumerable && value is not string)
        {
            return !enumerable.Cast<object>().Any();
        }

        string text = value.ToString() ?? string.Empty;
        return string.Equals(text, "0", StringComparison.Ordinal)
            || string.Equals(text, string.Empty, StringComparison.Ordinal)
            || string.Equals(text, "[]", StringComparison.Ordinal)
            || string.Equals(text, "Count = 0", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("System.Collections.", StringComparison.Ordinal);
    }

    private static bool PathMatches(string rulePath, string? propertyPath)
    {
        rulePath = NormalizeComparisonPath(rulePath);
        propertyPath = NormalizeComparisonPath(propertyPath);

        if (string.IsNullOrWhiteSpace(rulePath) || string.IsNullOrWhiteSpace(propertyPath))
        {
            return false;
        }

        if (string.Equals(rulePath, propertyPath, StringComparison.OrdinalIgnoreCase)
            || propertyPath.StartsWith(rulePath + ".", StringComparison.OrdinalIgnoreCase)
            || propertyPath.StartsWith(rulePath + "[", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        Regex regex = PathRulePatternCache.GetOrAdd(rulePath, static key =>
        {
            string pattern = "^" + Regex.Escape(key)
                .Replace("\\[\\*\\]", "\\[\\d+\\]", StringComparison.Ordinal)
                .Replace("\\*", ".*", StringComparison.Ordinal) + "(?:\\[\\d+\\])?(\\..*)?$";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
        });

        return regex.IsMatch(propertyPath);
    }

    private static bool MatchesPattern(string propertyPath, string pattern)
    {
        try
        {
            Regex regex = NameRulePatternCache.GetOrAdd(
                pattern,
                static (key, timeout) => new Regex(key, RegexOptions.IgnoreCase | RegexOptions.Compiled, timeout),
                RegexTimeout);
            return regex.IsMatch(propertyPath);
        }
        catch (ArgumentException)
        {
            Regex wildcardRegex = WildcardRulePatternCache.GetOrAdd(pattern, static key =>
            {
                string wildcardPattern = "^" + Regex.Escape(key)
                    .Replace("\\*", ".*", StringComparison.Ordinal)
                    .Replace("\\?", ".", StringComparison.Ordinal) + "$";
                return new Regex(wildcardPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            });

            return wildcardRegex.IsMatch(propertyPath);
        }
    }

    private static string GetLeafPropertyName(string propertyPath)
    {
        string normalized = NormalizeComparisonPath(propertyPath);
        int separatorIndex = normalized.LastIndexOf('.');
        ReadOnlySpan<char> leaf = normalized.AsSpan(separatorIndex + 1);
        int collectionIndex = leaf.IndexOf('[');
        return collectionIndex < 0 ? leaf.ToString() : leaf[..collectionIndex].ToString();
    }

    private static IReadOnlyList<ComparisonDifference> MaterializeDifferences(
        IEnumerable<Difference> differences,
        ComparisonOptions options)
    {
        PreparedComparisonRules rules = GetPreparedRules(options);
        HashSet<DifferenceKey> seen = new(DifferenceKeyComparer.Instance);
        List<ComparisonDifference> materialized = new();
        int limit = options.IncludeAllDifferences ? int.MaxValue : options.MaxDifferences;

        foreach (Difference difference in differences)
        {
            string path = GetDomainDifferencePropertyPath(difference);
            if (ShouldFilterDifference(difference, path, options, rules))
            {
                continue;
            }

            string? valueA = difference.Object1Value?.ToString();
            string? valueB = difference.Object2Value?.ToString();
            if (!seen.Add(new DifferenceKey(path, valueA, valueB)))
            {
                continue;
            }

            materialized.Add(new ComparisonDifference(path, valueA, valueB, difference.ToString()));
            if (materialized.Count == limit)
            {
                break;
            }
        }

        return materialized;
    }

    private readonly record struct DifferenceKey(string Path, string? ValueA, string? ValueB);

    private sealed class DifferenceKeyComparer : IEqualityComparer<DifferenceKey>
    {
        public static readonly DifferenceKeyComparer Instance = new();

        public bool Equals(DifferenceKey x, DifferenceKey y) =>
            StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path)
            && string.Equals(x.ValueA, y.ValueA, StringComparison.Ordinal)
            && string.Equals(x.ValueB, y.ValueB, StringComparison.Ordinal);

        public int GetHashCode(DifferenceKey value) => HashCode.Combine(
            StringComparer.OrdinalIgnoreCase.GetHashCode(value.Path),
            value.ValueA,
            value.ValueB);
    }

    private static string GetDomainDifferencePropertyPath(Difference difference) =>
        string.IsNullOrWhiteSpace(difference.PropertyName) ? "Response" : NormalizeComparisonPath(difference.PropertyName);

    private static string NormalizeComparisonPath(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return string.Empty;
        }

        string normalizedPath = Regex.Replace(
            propertyPath.Trim(),
            @"\.System\.Collections(?:\.Generic)?\.(?:IList|ICollection|IEnumerable)(?:`\d+)?\.Item\[(\d+)\]",
            "[$1]",
            RegexOptions.IgnoreCase,
            RegexTimeout);

        normalizedPath = Regex.Replace(
            normalizedPath,
            @"^(?:Expected|Actual|Object1|Object2|Root)\.",
            string.Empty,
            RegexOptions.IgnoreCase,
            RegexTimeout);

        return normalizedPath;
    }

    private static bool IsSuccessStatusCode(int statusCode) => statusCode is >= 200 and <= 299;

    private sealed class PreparedComparisonRules
    {
        private readonly PreparedPathMatcher[] ignoredPaths;
        private readonly PreparedPathMatcher[] nullEmptyPaths;
        private readonly HashSet<string> smartPropertyNames;
        private readonly Regex[] smartNamePatterns;
        private readonly HashSet<string> smartPropertyTypes;

        public PreparedComparisonRules(ComparisonOptions options)
        {
            ignoredPaths = options.IgnoreRules
                .Where(rule => rule.IgnoreCompletely)
                .Select(rule => new PreparedPathMatcher(rule.PropertyPath))
                .ToArray();
            nullEmptyPaths = options.IgnoreRules
                .Where(rule => rule.TreatNullAndEmptyCollectionsAsEqual)
                .Select(rule => new PreparedPathMatcher(rule.PropertyPath))
                .ToArray();
            smartPropertyNames = options.SmartIgnoreRules
                .Where(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.PropertyName)
                .Select(rule => rule.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            smartNamePatterns = options.SmartIgnoreRules
                .Where(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.NamePattern)
                .Select(rule => CompileNamePattern(rule.Value))
                .ToArray();
            smartPropertyTypes = options.SmartIgnoreRules
                .Where(rule => rule.IsEnabled && rule.Kind == SmartIgnoreRuleKind.PropertyType)
                .Select(rule => rule.Value)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            IgnoreCollectionOrder = ShouldIgnoreCollectionOrder(options);
            RequiresPreparation = IgnoreCollectionOrder || ignoredPaths.Length > 0 || smartPropertyNames.Count > 0 || smartNamePatterns.Length > 0;
        }

        public bool IgnoreCollectionOrder { get; }

        public bool RequiresPreparation { get; }

        public bool ShouldIgnorePath(ReadOnlySpan<char> path)
        {
            foreach (PreparedPathMatcher matcher in ignoredPaths)
            {
                if (matcher.IsMatch(path)) return true;
            }

            return false;
        }

        public bool ShouldIgnoreChild(ReadOnlySpan<char> parentPath, string propertyName)
        {
            foreach (PreparedPathMatcher matcher in ignoredPaths)
            {
                if (matcher.IsDirectChildMatch(parentPath, propertyName)) return true;
            }

            return false;
        }

        public bool ShouldIgnoreSmartPropertyName(string propertyName) => smartPropertyNames.Contains(propertyName);

        public bool ShouldTreatNullAndEmptyAsEqual(string path)
        {
            foreach (PreparedPathMatcher matcher in nullEmptyPaths)
            {
                if (matcher.IsMatch(path)) return true;
            }

            return false;
        }

        public bool ShouldIgnoreCollectionElement(ReadOnlySpan<char> parentPath, int index)
        {
            foreach (PreparedPathMatcher matcher in ignoredPaths)
            {
                if (matcher.IsCollectionElementMatch(parentPath, index)) return true;
            }

            return false;
        }

        public bool ShouldIgnoreSmartPath(ReadOnlySpan<char> path)
        {
            if (path.IsEmpty || path.IsWhiteSpace())
            {
                return false;
            }

            ReadOnlySpan<char> leaf = GetPreparedLeafPropertyName(path);

            foreach (string propertyName in smartPropertyNames)
            {
                if (leaf.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            foreach (Regex pattern in smartNamePatterns)
            {
                if (pattern.IsMatch(path.ToString()))
                {
                    return true;
                }
            }

            return false;
        }

        private static ReadOnlySpan<char> GetPreparedLeafPropertyName(ReadOnlySpan<char> path)
        {
            ReadOnlySpan<char> leaf = path[(path.LastIndexOf('.') + 1)..];
            int collectionIndex = leaf.IndexOf('[');
            return collectionIndex < 0 ? leaf : leaf[..collectionIndex];
        }

        public bool ShouldIgnoreSmartDifference(Difference difference, string normalizedPath)
        {
            if (ShouldIgnoreSmartPath(normalizedPath))
            {
                return true;
            }

            object? value = difference.Object1Value ?? difference.Object2Value;
            Type? type = value?.GetType();
            return type is not null
                && (smartPropertyTypes.Contains(type.Name)
                    || (type.FullName is not null && smartPropertyTypes.Contains(type.FullName)));
        }

        private static Regex CompileNamePattern(string pattern)
        {
            try
            {
                return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            }
            catch (ArgumentException)
            {
                string wildcardPattern = "^" + Regex.Escape(pattern)
                    .Replace("\\*", ".*", StringComparison.Ordinal)
                    .Replace("\\?", ".", StringComparison.Ordinal) + "$";
                return new Regex(wildcardPattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            }
        }
    }

    private sealed class PreparedPathMatcher
    {
        private readonly string path;
        private readonly Regex? wildcard;
        private readonly bool hasCollectionWildcard;

        public PreparedPathMatcher(string value)
        {
            path = NormalizeComparisonPath(value);
            hasCollectionWildcard = path.Contains("[*]", StringComparison.Ordinal);
            if (path.Contains('*', StringComparison.Ordinal) && !hasCollectionWildcard)
            {
                string pattern = "^" + Regex.Escape(path)
                    .Replace("\\[\\*\\]", "\\[\\d+\\]", StringComparison.Ordinal)
                    .Replace("\\*", ".*", StringComparison.Ordinal) + "(?:\\[\\d+\\])?(\\..*)?$";
                wildcard = new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);
            }
        }

        public bool IsMatch(ReadOnlySpan<char> candidate)
        {
            if (hasCollectionWildcard)
            {
                return MatchesCollectionWildcard(path, candidate);
            }

            if (candidate.StartsWith(path, StringComparison.OrdinalIgnoreCase)
                && (candidate.Length == path.Length
                    || candidate.Length > path.Length && candidate[path.Length] is '.' or '['))
            {
                return true;
            }

            return wildcard?.IsMatch(candidate.ToString()) == true;
        }

        public bool IsDirectChildMatch(ReadOnlySpan<char> parentPath, string propertyName)
        {
            int separatorIndex = path.LastIndexOf('.');
            if (separatorIndex < 0
                || !path.AsSpan(separatorIndex + 1).Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            ReadOnlySpan<char> parentPattern = path.AsSpan(0, separatorIndex);
            return hasCollectionWildcard
                ? MatchesCollectionWildcard(parentPattern, parentPath)
                : parentPattern.Equals(parentPath, StringComparison.OrdinalIgnoreCase);
        }

        public bool IsCollectionElementMatch(ReadOnlySpan<char> parentPath, int index)
        {
            if (!hasCollectionWildcard || !path.EndsWith("[*]", StringComparison.Ordinal))
            {
                return false;
            }

            return path.AsSpan(0, path.Length - 3).Equals(parentPath, StringComparison.OrdinalIgnoreCase);
        }

        private static bool MatchesCollectionWildcard(ReadOnlySpan<char> pattern, ReadOnlySpan<char> candidate)
        {
            int patternIndex = 0;
            int candidateIndex = 0;
            while (patternIndex < pattern.Length)
            {
                if (patternIndex + 2 < pattern.Length
                    && pattern[patternIndex] == '['
                    && pattern[patternIndex + 1] == '*'
                    && pattern[patternIndex + 2] == ']')
                {
                    if (candidateIndex >= candidate.Length || candidate[candidateIndex++] != '[')
                    {
                        return false;
                    }

                    int digitStart = candidateIndex;
                    while (candidateIndex < candidate.Length && char.IsAsciiDigit(candidate[candidateIndex]))
                    {
                        candidateIndex++;
                    }

                    if (candidateIndex == digitStart
                        || candidateIndex >= candidate.Length
                        || candidate[candidateIndex++] != ']')
                    {
                        return false;
                    }

                    patternIndex += 3;
                    continue;
                }

                if (candidateIndex >= candidate.Length
                    || char.ToUpperInvariant(pattern[patternIndex]) != char.ToUpperInvariant(candidate[candidateIndex]))
                {
                    return false;
                }

                patternIndex++;
                candidateIndex++;
            }

            return candidateIndex == candidate.Length
                || candidate[candidateIndex] is '.' or '[';
        }
    }

    private sealed class ComparisonModelPreparation : IDisposable
    {
        private static readonly ConcurrentDictionary<Type, TypePreparationPlan> TypePlans = new();
        private readonly SegmentedMutationLog mutations = new();
        private readonly DetailedCompareMetricsCollector? timing;
        private readonly PreparationWorkTiming? workTiming;
        private bool disposed;

        private ComparisonModelPreparation(DetailedCompareMetricsCollector? timing)
        {
            this.timing = timing;
            workTiming = timing is null ? null : new PreparationWorkTiming();
            ModelA = null!;
            ModelB = null!;
        }

        private void Initialize(object modelA, object modelB, ComparisonOptions options, PreparedComparisonRules rules)
        {
            if (!rules.RequiresPreparation)
            {
                ModelA = modelA;
                ModelB = modelB;
                return;
            }

            Stopwatch? stopwatch = timing is null ? null : Stopwatch.StartNew();
            try
            {
                Dictionary<object, object> visitedA = ObjectMapPool.Rent();
                Dictionary<object, object> visitedB = ObjectMapPool.Rent();
                try
                {
                    using PathCursor path = new();
                    ModelA = PrepareValue(modelA, path, options, rules, visitedA) ?? modelA;
                    ModelB = PrepareValue(modelB, path, options, rules, visitedB) ?? modelB;
                }
                finally
                {
                    ObjectMapPool.Return(visitedA);
                    ObjectMapPool.Return(visitedB);
                }
            }
            finally
            {
                if (stopwatch is not null)
                {
                    stopwatch.Stop();
                    TimeSpan classified = workTiming!.SortKeyDuration
                        + workTiming.SortDuration
                        + workTiming.FallbackDuration;
                    timing!.AddNormalizationTraversal(stopwatch.Elapsed > classified ? stopwatch.Elapsed - classified : TimeSpan.Zero);
                    timing.AddNormalizationSortKey(workTiming.SortKeyDuration);
                    timing.AddNormalizationSort(workTiming.SortDuration);
                    timing.AddNormalizationFallback(workTiming.FallbackDuration);
                }
            }
        }

        public object ModelA { get; private set; }

        public object ModelB { get; private set; }


        public static ComparisonModelPreparation Create(
            object modelA,
            object modelB,
            ComparisonOptions options,
            PreparedComparisonRules rules,
            DetailedCompareMetricsCollector? timing)
        {
            ComparisonModelPreparation preparation = new(timing);
            try
            {
                preparation.Initialize(modelA, modelB, options, rules);
                return preparation;
            }
            catch
            {
                preparation.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            Stopwatch? stopwatch = timing is null ? null : Stopwatch.StartNew();
            try
            {
                mutations.RestoreAndReturn();
            }
            finally
            {
                if (stopwatch is not null)
                {
                    stopwatch.Stop();
                    timing!.AddNormalizationRestoration(stopwatch.Elapsed);
                }
            }
        }

        private object? PrepareValue(
            object? value,
            PathCursor path,
            ComparisonOptions options,
            PreparedComparisonRules rules,
            Dictionary<object, object> visited)
        {
            if (value is null)
            {
                return value;
            }

            timing?.RecordStructureNode(path.Value, value.GetType(), path.Depth);

            if (rules.ShouldIgnorePath(path.Value) || rules.ShouldIgnoreSmartPath(path.Value))
            {
                timing?.AddIgnoredNode();
                return LegacyComparisonModelNormalizer.GetDefaultValue(value.GetType());
            }

            if (LegacyComparisonModelNormalizer.IsSimpleValue(value.GetType()))
            {
                int scalarBytes = GetScalarUtf8Length(value);
                timing?.AddScalarNode(scalarBytes);
                timing?.RecordScalarByteLength(scalarBytes);
                return value;
            }

            if (visited.TryGetValue(value, out object? existing))
            {
                return existing;
            }

            Type type = value.GetType();
            if (type.IsArray)
            {
                return PrepareArray((Array)value, path, options, rules, visited);
            }

            if (value is IDictionary dictionary && !dictionary.IsReadOnly)
            {
                return PrepareDictionary(dictionary, path, options, rules, visited);
            }

            if (value is IList list && !list.IsReadOnly)
            {
                return PrepareList(list, path, options, rules, visited);
            }

            if (value is IEnumerable && value is not string)
            {
                timing?.AddLegacyFallbackBranch();
                object clone = NormalizeFallback(value, path, options);
                visited[value] = clone;
                return clone;
            }

            TypePreparationPlan typePlan = TypePlans.GetOrAdd(type, static candidate => TypePreparationPlan.Create(candidate));
            if (!typePlan.CanMutateInPlace)
            {
                timing?.AddLegacyFallbackBranch();
                object clone = NormalizeFallback(value, path, options);
                visited[value] = clone;
                return clone;
            }

            visited[value] = value;
            timing?.AddObjectNode();
            timing?.AddMutableBranch();
            foreach (PreparedProperty property in typePlan.Properties)
            {
                timing?.AddPropertyNode();
                object? original = property.Get(value);
                object? prepared = property.IsJsonIgnored
                    || rules.ShouldIgnoreSmartPropertyName(property.Name)
                    || rules.ShouldIgnoreChild(path.Value, property.Name)
                    ? LegacyComparisonModelNormalizer.GetDefaultValue(property.PropertyType)
                    : PrepareChildProperty(original, property.Name, path, options, rules, visited);

                if (property.IsJsonIgnored
                    || rules.ShouldIgnoreSmartPropertyName(property.Name)
                    || rules.ShouldIgnoreChild(path.Value, property.Name))
                {
                    timing?.AddIgnoredNode();
                }

                if (!ReferenceEquals(original, prepared) && !Equals(original, prepared))
                {
                    mutations.Add(ModelMutation.ForProperty(value, property, original));
                    property.Set(value, prepared);
                }
            }

            return value;
        }

        private object? PrepareChildProperty(
            object? value,
            string propertyName,
            PathCursor path,
            ComparisonOptions options,
            PreparedComparisonRules rules,
            Dictionary<object, object> visited)
        {
            int restoreLength = path.PushProperty(propertyName);
            try
            {
                return PrepareValue(value, path, options, rules, visited);
            }
            finally
            {
                path.Restore(restoreLength);
            }
        }

        private object NormalizeFallback(object value, PathCursor path, ComparisonOptions options)
        {
            if (workTiming is null) return LegacyComparisonModelNormalizer.NormalizeBranch(value, path.ToString(), options);
            Stopwatch stopwatch = Stopwatch.StartNew();
            try
            {
                return LegacyComparisonModelNormalizer.NormalizeBranch(value, path.ToString(), options);
            }
            finally
            {
                stopwatch.Stop();
                workTiming.AddFallback(stopwatch.Elapsed);
            }
        }

        private object PrepareArray(Array array, PathCursor path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            PooledSnapshot<object?> snapshot = PooledSnapshot<object?>.Rent(array.Length);
            for (int index = 0; index < array.Length; index++) snapshot.Buffer[index] = array.GetValue(index);
            mutations.Add(ModelMutation.ForArray(array, snapshot));
            visited[array] = array;
            timing?.AddCollectionNode(snapshot.Count);
            timing?.RecordCollectionLength(snapshot.Count);
            using PooledSnapshot<object?> prepared = PooledSnapshot<object?>.Rent(snapshot.Count);
            for (int index = 0; index < snapshot.Count; index++)
                prepared.Buffer[index] = PrepareCollectionItem(snapshot.Buffer[index], path, index, options, rules, visited);

            if (rules.IgnoreCollectionOrder) SortPreparedItems(prepared.Buffer, prepared.Count);

            for (int index = 0; index < prepared.Count; index++) array.SetValue(prepared.Buffer[index], index);

            return array;
        }

        private object PrepareList(IList list, PathCursor path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            PooledSnapshot<object?> snapshot = PooledSnapshot<object?>.Rent(list.Count);
            for (int index = 0; index < list.Count; index++) snapshot.Buffer[index] = list[index];
            mutations.Add(ModelMutation.ForList(list, snapshot));
            visited[list] = list;
            timing?.AddCollectionNode(snapshot.Count);
            timing?.RecordCollectionLength(snapshot.Count);
            using PooledSnapshot<object?> prepared = PooledSnapshot<object?>.Rent(snapshot.Count);
            for (int index = 0; index < snapshot.Count; index++)
                prepared.Buffer[index] = PrepareCollectionItem(snapshot.Buffer[index], path, index, options, rules, visited);

            if (rules.IgnoreCollectionOrder) SortPreparedItems(prepared.Buffer, prepared.Count);

            for (int index = 0; index < prepared.Count; index++) list[index] = prepared.Buffer[index];

            return list;
        }

        private object? PrepareCollectionItem(
            object? item,
            PathCursor path,
            int index,
            ComparisonOptions options,
            PreparedComparisonRules rules,
            Dictionary<object, object> visited)
        {
            if (item is null)
            {
                return null;
            }

            Type itemType = item.GetType();
            if (LegacyComparisonModelNormalizer.IsSimpleValue(itemType))
            {
                if (rules.ShouldIgnoreCollectionElement(path.Value, index))
                {
                    timing?.AddIgnoredNode();
                    return LegacyComparisonModelNormalizer.GetDefaultValue(itemType);
                }

                int scalarBytes = GetScalarUtf8Length(item);
                timing?.AddScalarNode(scalarBytes);
                timing?.RecordScalarByteLength(scalarBytes);
                return item;
            }

            int restoreLength = path.PushIndex(index);
            try
            {
                return PrepareValue(item, path, options, rules, visited);
            }
            finally
            {
                path.Restore(restoreLength);
            }
        }

        private object PrepareDictionary(IDictionary dictionary, PathCursor path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            PooledSnapshot<DictionaryEntry> snapshot = PooledSnapshot<DictionaryEntry>.Rent(dictionary.Count);
            int entryIndex = 0;
            foreach (DictionaryEntry entry in dictionary)
            {
                snapshot.Buffer[entryIndex++] = entry;
            }

            mutations.Add(ModelMutation.ForDictionary(dictionary, snapshot));
            visited[dictionary] = dictionary;
            timing?.AddCollectionNode(snapshot.Count);
            timing?.RecordCollectionLength(snapshot.Count);
            for (int index = 0; index < snapshot.Count; index++)
            {
                DictionaryEntry entry = snapshot.Buffer[index];
                string childName = Convert.ToString(entry.Key) ?? string.Empty;
                object? entryValue = entry.Value;
                if (entryValue is not null
                    && LegacyComparisonModelNormalizer.IsSimpleValue(entryValue.GetType())
                    && !rules.ShouldIgnoreChild(path.Value, childName)
                    && !rules.ShouldIgnoreSmartPropertyName(childName))
                {
                    int scalarBytes = GetScalarUtf8Length(entryValue);
                    timing?.AddScalarNode(scalarBytes);
                    timing?.RecordScalarByteLength(scalarBytes);
                    continue;
                }

                int restoreLength = path.PushProperty(childName);
                try
                {
                    dictionary[entry.Key] = PrepareValue(entryValue, path, options, rules, visited);
                }
                finally
                {
                    path.Restore(restoreLength);
                }
            }

            return dictionary;
        }

        private void SortPreparedItems(object?[] items, int count)
        {
            if (count < 2)
            {
                return;
            }

            try
            {
                using PooledSnapshot<object?>? primaryValues = TryGetPrimarySortValues(items, count);
                using PooledSortKeyBatch keys = PooledSortKeyBatch.Create(primaryValues?.Buffer ?? items, count, timing, workTiming);
                long sortKeyTicksBefore = workTiming?.SortKeyTicks ?? 0;
                Stopwatch? sortStopwatch = workTiming is null ? null : Stopwatch.StartNew();
                PooledSnapshot<object?>? originals = primaryValues is null ? null : PooledSnapshot<object?>.Copy(items, count);
                try
                {
                    keys.SortInto(items, resolveCollisionsWith: originals?.Buffer);
                }
                finally
                {
                    originals?.Dispose();
                }
                if (sortStopwatch is not null)
                {
                    sortStopwatch.Stop();
                    long nestedSortKeyTicks = workTiming!.SortKeyTicks - sortKeyTicksBefore;
                    workTiming.AddSort(TimeSpan.FromTicks(Math.Max(0, sortStopwatch.ElapsedTicks - nestedSortKeyTicks)));
                }
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                Stopwatch? fallbackStopwatch = workTiming is null ? null : Stopwatch.StartNew();
                using PooledSortKeySet keys = new(items, count);
                using PooledSnapshot<object?> original = PooledSnapshot<object?>.Copy(items, count);
                int[] order = ArrayPool<int>.Shared.Rent(count);
                try
                {
                    for (int index = 0; index < count; index++) order[index] = index;
                    Array.Sort(order, 0, count, Comparer<int>.Create((left, right) =>
                    {
                        int comparison = PooledSortKeyComparer.Instance.Compare(keys.Items[left], keys.Items[right]);
                        return comparison != 0 ? comparison : left.CompareTo(right);
                    }));
                    for (int index = 0; index < count; index++) items[index] = original.Buffer[order[index]];
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(order, clearArray: true);
                }
                if (fallbackStopwatch is not null)
                {
                    fallbackStopwatch.Stop();
                    workTiming!.AddFallback(fallbackStopwatch.Elapsed);
                }
            }
        }

        private static PooledSnapshot<object?>? TryGetPrimarySortValues(object?[] items, int count)
        {
            Type? itemType = items[0]?.GetType();
            if (itemType is null
                || LegacyComparisonModelNormalizer.IsSimpleValue(itemType)
                || typeof(IEnumerable).IsAssignableFrom(itemType)
                || !AcyclicSortTypeCache.GetOrAdd(itemType, static type => IsStaticallyAcyclic(type, new HashSet<Type>(), new HashSet<Type>()))
                || ContainsDifferentRuntimeType(items, count, itemType))
            {
                return null;
            }

            SortPropertyPlan[] properties = SortPropertyPlans.GetOrAdd(itemType, CreateSortPropertyPlans);
            if (properties.Length == 0)
            {
                return null;
            }

            PooledSnapshot<object?> primaryValues = PooledSnapshot<object?>.Rent(count);
            for (int index = 0; index < count; index++)
            {
                primaryValues.Buffer[index] = properties[0].Get(items[index]!);
            }

            return primaryValues;
        }

        private static bool ContainsDifferentRuntimeType(object?[] items, int count, Type itemType)
        {
            for (int index = 0; index < count; index++)
            {
                if (items[index]?.GetType() != itemType) return true;
            }

            return false;
        }

        private static bool IsStaticallyAcyclic(Type type, HashSet<Type> visiting, HashSet<Type> visited)
        {
            if (LegacyComparisonModelNormalizer.IsSimpleValue(type) || visited.Contains(type)) { return true; }
            if (type == typeof(object) || type.IsInterface || !visiting.Add(type)) { return false; }
            try
            {
                Type? elementType = type.IsArray
                    ? type.GetElementType()
                    : type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                        ? type.GetGenericArguments().LastOrDefault()
                        : null;
                if (elementType is not null)
                {
                    if (!IsStaticallyAcyclic(elementType, visiting, visited)) { return false; }
                }
                else
                {
                    foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                        .Where(property => property.CanRead && property.GetIndexParameters().Length == 0))
                    {
                        if (!IsStaticallyAcyclic(property.PropertyType, visiting, visited)) { return false; }
                    }
                }

                visited.Add(type);
                return true;
            }
            finally
            {
                visiting.Remove(type);
            }
        }

        private static int GetScalarUtf8Length(object value)
        {
            if (value is string text) return Encoding.UTF8.GetByteCount(text);
            if (value is byte[] bytes) return bytes.Length;
            if (value is char character)
            {
                Span<char> characterBuffer = stackalloc char[1];
                characterBuffer[0] = character;
                return Encoding.UTF8.GetByteCount(characterBuffer);
            }
            if (value is Uri uri) return Encoding.UTF8.GetByteCount(uri.OriginalString);
            if (value is IUtf8SpanFormattable formattable)
            {
                Span<byte> buffer = stackalloc byte[128];
                if (formattable.TryFormat(buffer, out int written, default, CultureInfo.InvariantCulture)) return written;
            }

            return 0;
        }
    }

    private sealed class PreparationWorkTiming
    {
        public long SortKeyTicks { get; private set; }
        public long SortTicks { get; private set; }
        public long FallbackTicks { get; private set; }
        public TimeSpan SortKeyDuration => TimeSpan.FromTicks(SortKeyTicks);
        public TimeSpan SortDuration => TimeSpan.FromTicks(SortTicks);
        public TimeSpan FallbackDuration => TimeSpan.FromTicks(FallbackTicks);

        public void AddSortKey(TimeSpan elapsed) => SortKeyTicks += elapsed.Ticks;
        public void AddSort(TimeSpan elapsed) => SortTicks += elapsed.Ticks;
        public void AddFallback(TimeSpan elapsed) => FallbackTicks += elapsed.Ticks;
    }

    private sealed class PathCursor : IDisposable
    {
        private const int MaximumPooledLength = 16 * 1024;
        private char[] buffer = ArrayPool<char>.Shared.Rent(256);
        private bool pooled = true;
        private int length;
        private int depth;

        public ReadOnlySpan<char> Value => buffer.AsSpan(0, length);
        public int Depth => depth;

        public int PushProperty(string propertyName)
        {
            int restoreLength = length;
            int separatorLength = length == 0 ? 0 : 1;
            Ensure(separatorLength + propertyName.Length);
            if (separatorLength != 0) buffer[length++] = '.';
            propertyName.AsSpan().CopyTo(buffer.AsSpan(length));
            length += propertyName.Length;
            depth++;
            return restoreLength;
        }

        public int PushIndex(int index)
        {
            int restoreLength = length;
            Ensure(13);
            buffer[length++] = '[';
            index.TryFormat(buffer.AsSpan(length), out int written, provider: CultureInfo.InvariantCulture);
            length += written;
            buffer[length++] = ']';
            depth++;
            return restoreLength;
        }

        public void Restore(int restoreLength)
        {
            length = restoreLength;
            depth--;
        }

        public override string ToString() => new(Value);

        public void Dispose()
        {
            char[] returned = Interlocked.Exchange(ref buffer, Array.Empty<char>());
            if (pooled && returned.Length > 0) ArrayPool<char>.Shared.Return(returned, clearArray: true);
            length = 0;
            depth = 0;
            pooled = false;
        }

        private void Ensure(int additionalLength)
        {
            int required = checked(length + additionalLength);
            if (required <= buffer.Length) return;

            int doubled = buffer.Length <= MaximumPooledLength / 2 ? buffer.Length * 2 : MaximumPooledLength;
            int nextLength = Math.Max(required, doubled);
            bool nextPooled = nextLength <= MaximumPooledLength;
            char[] replacement = nextPooled ? ArrayPool<char>.Shared.Rent(nextLength) : new char[nextLength];
            buffer.AsSpan(0, length).CopyTo(replacement);
            if (pooled) ArrayPool<char>.Shared.Return(buffer, clearArray: true);
            buffer = replacement;
            pooled = nextPooled;
        }
    }

    private sealed class PooledSortKeyBatch : IDisposable
    {
        private const int MaximumInitialBufferLength = 64 * 1024;
        private readonly SegmentedByteBufferWriter output;
        private readonly PooledSnapshot<BatchedSortEntry> entries;
        private readonly DetailedCompareMetricsCollector? timing;
        private readonly PreparationWorkTiming? workTiming;

        private PooledSortKeyBatch(
            SegmentedByteBufferWriter output,
            PooledSnapshot<BatchedSortEntry> entries,
            DetailedCompareMetricsCollector? timing,
            PreparationWorkTiming? workTiming)
        {
            this.output = output;
            this.entries = entries;
            this.timing = timing;
            this.workTiming = workTiming;
        }

        public static PooledSortKeyBatch Create(
            object?[] values,
            int count,
            DetailedCompareMetricsCollector? timing = null,
            PreparationWorkTiming? workTiming = null)
        {
            Stopwatch? stopwatch = workTiming is null ? null : Stopwatch.StartNew();
            int initialCapacity = Math.Min(
                MaximumInitialBufferLength,
                Math.Max(256, count > MaximumInitialBufferLength / 256
                    ? MaximumInitialBufferLength
                    : count * 256));
            SegmentedByteBufferWriter output = new(initialCapacity);
            PooledSnapshot<BatchedSortEntry> entries = PooledSnapshot<BatchedSortEntry>.Rent(count);
            try
            {
                HashSet<object> visited = ObjectSetPool.Rent();
                try
                {
                    using Utf8JsonWriter writer = new(output);
                    for (int index = 0; index < count; index++)
                    {
                        int offset = output.WrittenCount;
                        visited.Clear();
                        WriteSortValue(writer, values[index], visited);
                        writer.Flush();

                        int keyLength = output.WrittenCount - offset;
                        bool isAscii = output.IsAscii(offset, keyLength);
                        entries.Buffer[index] = new BatchedSortEntry(
                            values[index],
                            offset,
                            keyLength,
                            isAscii ? null : output.Decode(offset, keyLength),
                            index);
                        timing?.AddSortKeyBytes(keyLength);
                        timing?.RecordSortKeyByteLength(keyLength);
                        if (index + 1 < count) writer.Reset(output);
                    }
                }
                finally { ObjectSetPool.Return(visited); }

                return new PooledSortKeyBatch(output, entries, timing, workTiming);
            }
            catch
            {
                entries.Dispose();
                output.Dispose();
                throw;
            }
            finally
            {
                if (stopwatch is not null)
                {
                    stopwatch.Stop();
                    workTiming!.AddSortKey(stopwatch.Elapsed);
                }
            }
        }

        public void SortInto(object?[] destination, object?[]? resolveCollisionsWith = null)
        {
            BatchedSortEntryComparer comparer = new(output);
            Array.Sort(entries.Buffer, 0, entries.Count, comparer);
            if (resolveCollisionsWith is not null)
            {
                int start = 0;
                while (start < entries.Count)
                {
                    int end = start + 1;
                    while (end < entries.Count && comparer.CompareKey(entries.Buffer[start], entries.Buffer[end]) == 0) { end++; }
                    if (end - start > 1)
                    {
                        timing?.AddSortCollisionGroup();
                        using PooledSnapshot<object?> collisionValues = PooledSnapshot<object?>.Rent(end - start);
                        for (int index = start; index < end; index++)
                        {
                            collisionValues.Buffer[index - start] = resolveCollisionsWith[entries.Buffer[index].OriginalIndex];
                        }

                        using PooledSortKeyBatch fullKeys = Create(collisionValues.Buffer, collisionValues.Count, timing, workTiming);
                        fullKeys.SortInto(collisionValues.Buffer);
                        for (int index = start; index < end; index++)
                        {
                            entries.Buffer[index] = entries.Buffer[index] with { Value = collisionValues.Buffer[index - start] };
                        }
                    }

                    start = end;
                }
            }

            for (int index = 0; index < entries.Count; index++)
            {
                destination[index] = resolveCollisionsWith is null
                    ? entries.Buffer[index].Value
                    : resolveCollisionsWith[entries.Buffer[index].OriginalIndex];
            }

            if (resolveCollisionsWith is not null)
            {
                // Collision groups already carry fully sorted original values.
                int start = 0;
                while (start < entries.Count)
                {
                    int end = start + 1;
                    while (end < entries.Count && comparer.CompareKey(entries.Buffer[start], entries.Buffer[end]) == 0) { end++; }
                    if (end - start > 1)
                    {
                        for (int index = start; index < end; index++) destination[index] = entries.Buffer[index].Value;
                    }
                    start = end;
                }
            }
        }

        public void Dispose()
        {
            entries.Dispose();
            output.Dispose();
        }
    }

    private readonly record struct BatchedSortEntry(
        object? Value,
        int Offset,
        int Length,
        string? DecodedKey,
        int OriginalIndex);

    private sealed class BatchedSortEntryComparer(SegmentedByteBufferWriter output) : IComparer<BatchedSortEntry>
    {
        public int Compare(BatchedSortEntry x, BatchedSortEntry y)
        {
            int keyComparison = CompareKey(x, y);
            return keyComparison != 0 ? keyComparison : x.OriginalIndex.CompareTo(y.OriginalIndex);
        }

        public int CompareKey(BatchedSortEntry x, BatchedSortEntry y) =>
            x.DecodedKey is null && y.DecodedKey is null
                ? output.Compare(x.Offset, x.Length, y.Offset, y.Length)
                : string.CompareOrdinal(
                    x.DecodedKey ?? output.Decode(x.Offset, x.Length),
                    y.DecodedKey ?? output.Decode(y.Offset, y.Length));
    }

    private sealed record TypePreparationPlan(bool CanMutateInPlace, PreparedProperty[] Properties)
    {
        public static TypePreparationPlan Create(Type type)
        {
            PropertyInfo[] readable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
                .ToArray();
            // A getter-only diagnostic property that comparison already ignores must
            // not force the entire object graph through the allocation-heavy legacy
            // clone path. This is common on generated/client response contracts.
            bool canMutate = readable.All(property =>
                property.CanWrite || LegacyComparisonModelNormalizer.HasJsonIgnoreAttribute(property));
            return new TypePreparationPlan(
                canMutate,
                readable.Where(property => property.CanWrite)
                    .Select(property => PreparedProperty.Create(type, property))
                    .ToArray());
        }
    }

    private sealed record PreparedProperty(
        PropertyInfo Info,
        Func<object, object?> Get,
        Action<object, object?> Set,
        bool IsJsonIgnored)
    {
        public string Name => Info.Name;
        public Type PropertyType => Info.PropertyType;

        public static PreparedProperty Create(Type declaringType, PropertyInfo property)
        {
            if (declaringType.IsValueType)
            {
                return new PreparedProperty(
                    property,
                    property.GetValue,
                    property.SetValue,
                    LegacyComparisonModelNormalizer.HasJsonIgnoreAttribute(property));
            }

            ParameterExpression target = Expression.Parameter(typeof(object), "target");
            ParameterExpression value = Expression.Parameter(typeof(object), "value");
            Func<object, object?> getter = Expression.Lambda<Func<object, object?>>(
                Expression.Convert(Expression.Property(Expression.Convert(target, declaringType), property), typeof(object)),
                target).Compile();
            Action<object, object?> setter = Expression.Lambda<Action<object, object?>>(
                Expression.Assign(
                    Expression.Property(Expression.Convert(target, declaringType), property),
                    Expression.Convert(value, property.PropertyType)),
                target,
                value).Compile();
            return new PreparedProperty(
                property,
                getter,
                setter,
                LegacyComparisonModelNormalizer.HasJsonIgnoreAttribute(property));
        }
    }

    private readonly struct ModelMutation
    {
        private readonly object target;
        private readonly PreparedProperty? property;
        private readonly object? original;

        private ModelMutation(object target, PreparedProperty? property, object? original)
        {
            this.target = target;
            this.property = property;
            this.original = original;
        }

        public static ModelMutation ForProperty(object target, PreparedProperty property, object? original) => new(target, property, original);
        public static ModelMutation ForArray(Array target, PooledSnapshot<object?> original) => new(target, null, original);
        public static ModelMutation ForList(IList target, PooledSnapshot<object?> original) => new(target, null, original);
        public static ModelMutation ForDictionary(IDictionary target, PooledSnapshot<DictionaryEntry> original) => new(target, null, original);

        public void Restore()
        {
            if (property is not null)
            {
                property.Set(target, original);
                return;
            }

            if (target is Array array && original is PooledSnapshot<object?> arrayItems)
            {
                try
                {
                    for (int index = 0; index < arrayItems.Count; index++) array.SetValue(arrayItems.Buffer[index], index);
                }
                finally { arrayItems.Dispose(); }
            }
            else if (target is IList list && original is PooledSnapshot<object?> listItems)
            {
                try
                {
                    for (int index = 0; index < listItems.Count; index++) list[index] = listItems.Buffer[index];
                }
                finally { listItems.Dispose(); }
            }
            else if (target is IDictionary dictionary && original is PooledSnapshot<DictionaryEntry> entries)
            {
                try
                {
                    dictionary.Clear();
                    for (int index = 0; index < entries.Count; index++)
                    {
                        DictionaryEntry entry = entries.Buffer[index];
                        dictionary[entry.Key] = entry.Value;
                    }
                }
                finally { entries.Dispose(); }
            }
        }
    }

    private sealed class SegmentedMutationLog
    {
        private const int SegmentLength = 1024;
        private readonly List<ModelMutation[]> segments = new();
        private int count;

        public void Add(ModelMutation mutation)
        {
            int segmentIndex = count / SegmentLength;
            if (segmentIndex == segments.Count)
            {
                segments.Add(ArrayPool<ModelMutation>.Shared.Rent(SegmentLength));
            }

            segments[segmentIndex][count % SegmentLength] = mutation;
            count++;
        }

        public void RestoreAndReturn()
        {
            ExceptionDispatchInfo? firstFailure = null;
            try
            {
                for (int index = count - 1; index >= 0; index--)
                {
                    try
                    {
                        segments[index / SegmentLength][index % SegmentLength].Restore();
                    }
                    catch (Exception ex)
                    {
                        firstFailure ??= ExceptionDispatchInfo.Capture(ex);
                    }
                }
            }
            finally
            {
                foreach (ModelMutation[] segment in segments)
                {
                    Array.Clear(segment);
                    ArrayPool<ModelMutation>.Shared.Return(segment);
                }
                segments.Clear();
                count = 0;
            }

            firstFailure?.Throw();
        }
    }

    private static class ObjectMapPool
    {
        private const int MaximumRetainedMaps = 16;
        private static readonly ConcurrentBag<Dictionary<object, object>> Maps = new();
        private static int retainedCount;

        public static Dictionary<object, object> Rent()
        {
            if (!Maps.TryTake(out Dictionary<object, object>? map))
            {
                return new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            }

            Interlocked.Decrement(ref retainedCount);
            return map;
        }

        public static void Return(Dictionary<object, object> map)
        {
            map.Clear();
            int retained = Interlocked.Increment(ref retainedCount);
            if (retained <= MaximumRetainedMaps)
            {
                Maps.Add(map);
                return;
            }

            Interlocked.Decrement(ref retainedCount);
        }
    }

    private static class ObjectSetPool
    {
        private const int MaximumRetainedSets = 16;
        private static readonly ConcurrentBag<HashSet<object>> Sets = new();
        private static int retainedCount;

        public static HashSet<object> Rent()
        {
            if (!Sets.TryTake(out HashSet<object>? set))
                return new HashSet<object>(ReferenceEqualityComparer.Instance);
            Interlocked.Decrement(ref retainedCount);
            return set;
        }

        public static void Return(HashSet<object> set)
        {
            set.Clear();
            int retained = Interlocked.Increment(ref retainedCount);
            if (retained <= MaximumRetainedSets) Sets.Add(set);
            else Interlocked.Decrement(ref retainedCount);
        }
    }

    private sealed class PooledSnapshot<T> : IDisposable
    {
        private const int MaximumPooledLength = 8 * 1024;
        private T[] buffer;
        private readonly bool pooled;

        private PooledSnapshot(T[] buffer, int count, bool pooled)
        {
            this.buffer = buffer;
            Count = count;
            this.pooled = pooled;
        }

        public T[] Buffer => buffer;
        public int Count { get; }

        public static PooledSnapshot<T> Rent(int count)
        {
            bool usePool = count <= MaximumPooledLength;
            T[] rented = usePool ? ArrayPool<T>.Shared.Rent(Math.Max(1, count)) : new T[count];
            return new PooledSnapshot<T>(rented, count, usePool);
        }

        public static PooledSnapshot<T> Copy(T[] source, int count)
        {
            PooledSnapshot<T> snapshot = Rent(count);
            source.AsSpan(0, count).CopyTo(snapshot.buffer);
            return snapshot;
        }

        public void Dispose()
        {
            T[] returned = Interlocked.Exchange(ref buffer, Array.Empty<T>());
            if (pooled && returned.Length > 0) ArrayPool<T>.Shared.Return(returned, clearArray: true);
        }
    }

    private sealed class PooledSortKeySet : IDisposable
    {
        public PooledSortKeySet(object?[] values, int count)
        {
            Items = new PooledSortKey[count];
            for (int index = 0; index < count; index++) Items[index] = PooledSortKey.Create(values[index]);
        }
        public PooledSortKey[] Items { get; }
        public void Dispose()
        {
            foreach (PooledSortKey item in Items) { item.Dispose(); }
        }
    }

    private sealed class PooledSortKey : IDisposable
    {
        private byte[] buffer;
        private readonly bool isPooled;

        private PooledSortKey(byte[] buffer, int length, bool isAscii, bool isPooled)
        {
            this.buffer = buffer;
            Length = length;
            IsAscii = isAscii;
            this.isPooled = isPooled;
        }

        public int Length { get; }
        public bool IsAscii { get; }
        public ReadOnlySpan<byte> Bytes => buffer.AsSpan(0, Length);

        public static PooledSortKey Create(object? value)
        {
            PooledByteBufferWriter output = new();
            HashSet<object> visited = ObjectSetPool.Rent();
            try
            {
                using Utf8JsonWriter writer = new(output);
                WriteSortValue(writer, value, visited);
                writer.Flush();
                return output.Detach();
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                output.Dispose();
                byte[] fallback = Encoding.UTF8.GetBytes(value?.ToString() ?? string.Empty);
                ByteArrayRental rental = SortBufferPool.Rent(Math.Max(1, fallback.Length));
                fallback.CopyTo(rental.Buffer, 0);
                return new PooledSortKey(rental.Buffer, fallback.Length, fallback.All(item => item < 128), rental.IsPooled);
            }
            finally { ObjectSetPool.Return(visited); }
        }

        public void Dispose()
        {
            byte[] returned = Interlocked.Exchange(ref buffer, Array.Empty<byte>());
            if (returned.Length > 0) { SortBufferPool.Return(new ByteArrayRental(returned, isPooled)); }
        }

        public string Decode() => Encoding.UTF8.GetString(Bytes);

        public static PooledSortKey FromDetached(byte[] buffer, int length, bool ascii, bool isPooled) => new(buffer, length, ascii, isPooled);
    }

    private static readonly ConcurrentDictionary<Type, SortPropertyPlan[]> SortPropertyPlans = new();

    private static void WriteSortValue(Utf8JsonWriter writer, object? value, HashSet<object> visited)
    {
        if (value is null) { writer.WriteNullValue(); return; }
        switch (value)
        {
            case string text: writer.WriteStringValue(text); return;
            case bool boolean: writer.WriteBooleanValue(boolean); return;
            case byte number: writer.WriteNumberValue(number); return;
            case sbyte number: writer.WriteNumberValue(number); return;
            case short number: writer.WriteNumberValue(number); return;
            case ushort number: writer.WriteNumberValue(number); return;
            case int number: writer.WriteNumberValue(number); return;
            case uint number: writer.WriteNumberValue(number); return;
            case long number: writer.WriteNumberValue(number); return;
            case ulong number: writer.WriteNumberValue(number); return;
            case float number: writer.WriteNumberValue(number); return;
            case double number: writer.WriteNumberValue(number); return;
            case decimal number: writer.WriteNumberValue(number); return;
            case DateTime dateTime: writer.WriteStringValue(dateTime); return;
            case DateTimeOffset dateTimeOffset: writer.WriteStringValue(dateTimeOffset); return;
            case Guid guid: writer.WriteStringValue(guid); return;
            case Uri uri: writer.WriteStringValue(uri.OriginalString); return;
            case byte[] bytes: writer.WriteBase64StringValue(bytes); return;
        }

        Type type = value.GetType();
        if (type.IsEnum)
        {
            JsonSerializer.Serialize(writer, value, type);
            return;
        }

        if (!type.IsValueType && !visited.Add(value))
        {
            throw new JsonException("A possible object cycle was detected while producing a comparison sort key.");
        }

        try
        {
            if (value is IDictionary dictionary)
            {
                writer.WriteStartObject();
                foreach (DictionaryEntry entry in dictionary)
                {
                    writer.WritePropertyName(Convert.ToString(entry.Key, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty);
                    WriteSortValue(writer, entry.Value, visited);
                }
                writer.WriteEndObject();
                return;
            }

            if (value is IEnumerable enumerable)
            {
                writer.WriteStartArray();
                foreach (object? item in enumerable) { WriteSortValue(writer, item, visited); }
                writer.WriteEndArray();
                return;
            }

            SortPropertyPlan[] properties = SortPropertyPlans.GetOrAdd(type, CreateSortPropertyPlans);
            writer.WriteStartObject();
            foreach (SortPropertyPlan property in properties)
            {
                writer.WritePropertyName(property.JsonName);
                WriteSortValue(writer, property.Get(value), visited);
            }
            writer.WriteEndObject();
        }
        finally
        {
            if (!type.IsValueType) { visited.Remove(value); }
        }
    }

    private static SortPropertyPlan[] CreateSortPropertyPlans(Type candidate) =>
        candidate.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.CanRead
                && property.GetIndexParameters().Length == 0
                && !LegacyComparisonModelNormalizer.HasJsonIgnoreAttribute(property))
            .Select(property => SortPropertyPlan.Create(candidate, property))
            .ToArray();

    private sealed record SortPropertyPlan(string JsonName, Func<object, object?> Get)
    {
        public static SortPropertyPlan Create(Type declaringType, PropertyInfo property)
        {
            Func<object, object?> getter;
            if (declaringType.IsValueType)
            {
                getter = property.GetValue;
            }
            else
            {
                ParameterExpression target = Expression.Parameter(typeof(object), "target");
                getter = Expression.Lambda<Func<object, object?>>(
                    Expression.Convert(Expression.Property(Expression.Convert(target, declaringType), property), typeof(object)),
                    target).Compile();
            }

            return new SortPropertyPlan(property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name, getter);
        }
    }

    private sealed class PooledSortKeyComparer : IComparer<PooledSortKey>
    {
        public static readonly PooledSortKeyComparer Instance = new();
        public int Compare(PooledSortKey? x, PooledSortKey? y)
        {
            if (ReferenceEquals(x, y)) { return 0; }
            if (x is null) { return -1; }
            if (y is null) { return 1; }
            return x.IsAscii && y.IsAscii
                ? x.Bytes.SequenceCompareTo(y.Bytes)
                : string.CompareOrdinal(x.Decode(), y.Decode());
        }
    }

    /// <summary>
    /// Append-only writer built from bounded 64 KiB rentals. Large sort-key batches
    /// never grow or copy one contiguous LOH-sized array.
    /// </summary>
    private sealed class SegmentedByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private const int SegmentLength = 64 * 1024;
        private readonly List<Segment> segments = new();
        private Segment? current;
        private int written;
        private bool disposed;

        public SegmentedByteBufferWriter(int initialCapacity) => AllocateSegment(Math.Min(SegmentLength, Math.Max(256, initialCapacity)));

        public int WrittenCount => written;

        public void Advance(int count)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (current is null || count < 0 || count > current.Rental.Buffer.Length - current.Written)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            current.Written += count;
            written += count;
        }

        public Memory<byte> GetMemory(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return current!.Rental.Buffer.AsMemory(current.Written);
        }

        public Span<byte> GetSpan(int sizeHint = 0)
        {
            Ensure(sizeHint);
            return current!.Rental.Buffer.AsSpan(current.Written);
        }

        public bool IsAscii(int offset, int length)
        {
            ValidateSlice(offset, length);
            int remaining = length;
            int position = offset;
            while (remaining > 0)
            {
                (Segment segment, int localOffset) = Locate(position);
                int count = Math.Min(remaining, segment.Written - localOffset);
                if (segment.Rental.Buffer.AsSpan(localOffset, count).IndexOfAnyExceptInRange((byte)0, (byte)127) >= 0) return false;
                position += count;
                remaining -= count;
            }

            return true;
        }

        public int Compare(int leftOffset, int leftLength, int rightOffset, int rightLength)
        {
            ValidateSlice(leftOffset, leftLength);
            ValidateSlice(rightOffset, rightLength);
            int leftPosition = leftOffset;
            int rightPosition = rightOffset;
            int leftRemaining = leftLength;
            int rightRemaining = rightLength;
            while (leftRemaining > 0 && rightRemaining > 0)
            {
                (Segment left, int leftLocal) = Locate(leftPosition);
                (Segment right, int rightLocal) = Locate(rightPosition);
                int count = Math.Min(
                    Math.Min(leftRemaining, left.Written - leftLocal),
                    Math.Min(rightRemaining, right.Written - rightLocal));
                int comparison = left.Rental.Buffer.AsSpan(leftLocal, count)
                    .SequenceCompareTo(right.Rental.Buffer.AsSpan(rightLocal, count));
                if (comparison != 0) return comparison;
                leftPosition += count;
                rightPosition += count;
                leftRemaining -= count;
                rightRemaining -= count;
            }

            return leftLength.CompareTo(rightLength);
        }

        public string Decode(int offset, int length)
        {
            ValidateSlice(offset, length);
            Decoder counter = Encoding.UTF8.GetDecoder();
            int characterCount = 0;
            int remaining = length;
            int position = offset;
            while (remaining > 0)
            {
                (Segment segment, int localOffset) = Locate(position);
                int count = Math.Min(remaining, segment.Written - localOffset);
                remaining -= count;
                characterCount += counter.GetCharCount(
                    segment.Rental.Buffer.AsSpan(localOffset, count),
                    flush: remaining == 0);
                position += count;
            }
            return string.Create(
                characterCount,
                (Writer: this, Offset: offset, Length: length),
                static (destination, state) => state.Writer.DecodeInto(state.Offset, state.Length, destination));
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            foreach (Segment segment in segments) SortBufferPool.Return(segment.Rental);
            segments.Clear();
            current = null;
            written = 0;
        }

        private void DecodeInto(int offset, int length, Span<char> destination)
        {
            Decoder decoder = Encoding.UTF8.GetDecoder();
            int destinationOffset = 0;
            int remaining = length;
            int position = offset;
            while (remaining > 0)
            {
                (Segment segment, int localOffset) = Locate(position);
                int count = Math.Min(remaining, segment.Written - localOffset);
                remaining -= count;
                destinationOffset += decoder.GetChars(
                    segment.Rental.Buffer.AsSpan(localOffset, count),
                    destination[destinationOffset..],
                    flush: remaining == 0);
                position += count;
            }
        }

        private (Segment Segment, int LocalOffset) Locate(int offset)
        {
            for (int index = segments.Count - 1; index >= 0; index--)
            {
                Segment segment = segments[index];
                if (offset >= segment.Start) return (segment, offset - segment.Start);
            }

            throw new ArgumentOutOfRangeException(nameof(offset));
        }

        private void Ensure(int sizeHint)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            int required = Math.Max(1, sizeHint);
            if (current is null || current.Rental.Buffer.Length - current.Written < required)
            {
                AllocateSegment(Math.Max(SegmentLength, required));
            }
        }

        private void AllocateSegment(int minimumLength)
        {
            ByteArrayRental rental = SortBufferPool.Rent(minimumLength);
            current = new Segment(written, rental);
            segments.Add(current);
        }

        private void ValidateSlice(int offset, int length)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (offset < 0 || length < 0 || offset > written - length) throw new ArgumentOutOfRangeException(nameof(offset));
        }

        private sealed class Segment(int start, ByteArrayRental rental)
        {
            public int Start { get; } = start;
            public ByteArrayRental Rental { get; } = rental;
            public int Written { get; set; }
        }
    }

    private sealed class PooledByteBufferWriter : IBufferWriter<byte>, IDisposable
    {
        private byte[] buffer;
        private bool isPooled;
        private int written;

        public PooledByteBufferWriter(int initialCapacity = 256)
        {
            ByteArrayRental rental = SortBufferPool.Rent(Math.Max(1, initialCapacity));
            buffer = rental.Buffer;
            isPooled = rental.IsPooled;
        }

        public void Advance(int count) => written += count;
        public Memory<byte> GetMemory(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsMemory(written); }
        public Span<byte> GetSpan(int sizeHint = 0) { Ensure(sizeHint); return buffer.AsSpan(written); }
        public int WrittenCount => written;
        public ReadOnlySpan<byte> WrittenSpan => buffer.AsSpan(0, written);

        public PooledSortKey Detach()
        {
            byte[] detached = buffer;
            bool detachedIsPooled = isPooled;
            int length = written;
            bool ascii = detached.AsSpan(0, length).IndexOfAnyExceptInRange((byte)0, (byte)127) < 0;
            buffer = Array.Empty<byte>();
            isPooled = false;
            written = 0;
            return PooledSortKey.FromDetached(detached, length, ascii, detachedIsPooled);
        }

        public void Dispose()
        {
            if (buffer.Length > 0) { SortBufferPool.Return(new ByteArrayRental(buffer, isPooled)); }
            buffer = Array.Empty<byte>();
            isPooled = false;
        }

        private void Ensure(int sizeHint)
        {
            int required = written + Math.Max(1, sizeHint);
            if (required <= buffer.Length) { return; }
            int growth = buffer.Length > int.MaxValue / 2 ? int.MaxValue : buffer.Length * 2;
            ByteArrayRental replacement = SortBufferPool.Rent(Math.Max(required, growth));
            buffer.AsSpan(0, written).CopyTo(replacement.Buffer);
            SortBufferPool.Return(new ByteArrayRental(buffer, isPooled));
            buffer = replacement.Buffer;
            isPooled = replacement.IsPooled;
        }
    }

    private static class LegacyComparisonModelNormalizer
    {
        public static object Normalize(object model, ComparisonOptions options)
        {
            if (!ShouldNormalize(options))
            {
                return model;
            }

            Dictionary<object, object> visited = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);
            return NormalizeValue(model, string.Empty, options, visited) ?? model;
        }

        public static object NormalizeBranch(object model, string path, ComparisonOptions options)
        {
            Dictionary<object, object> visited = new(ReferenceEqualityComparer.Instance);
            return NormalizeValue(model, path, options, visited) ?? model;
        }

        private static bool ShouldNormalize(ComparisonOptions options) =>
            ShouldIgnoreCollectionOrder(options)
            || options.IgnoreRules.Any(rule => rule.IgnoreCompletely)
            || options.SmartIgnoreRules.Any(rule => rule.IsEnabled);

        private static object? NormalizeValue(
            object? value,
            string path,
            ComparisonOptions options,
            Dictionary<object, object> visited)
        {
            if (value is null || IsSimpleValue(value.GetType()))
            {
                return value;
            }

            if (ShouldIgnoreByRule(path, options) || ShouldIgnoreBySmartPath(path, options))
            {
                return GetDefaultValue(value.GetType());
            }

            if (visited.TryGetValue(value, out object? existing))
            {
                return existing;
            }

            Type type = value.GetType();
            if (type.IsArray)
            {
                Array source = (Array)value;
                Type elementType = type.GetElementType() ?? typeof(object);
                object?[] items = source
                    .Cast<object?>()
                    .Select((item, index) => NormalizeValue(item, $"{path}[{index}]", options, visited))
                    .ToArray();

                if (ShouldIgnoreCollectionOrder(options))
                {
                    items = items.OrderBy(CreateSortKey, StringComparer.Ordinal).ToArray();
                }

                Array clone = Array.CreateInstance(elementType, items.Length);
                visited[value] = clone;
                for (int index = 0; index < items.Length; index++)
                {
                    clone.SetValue(items[index], index);
                }

                return clone;
            }

            if (value is IDictionary dictionary)
            {
                IDictionary clone = CreateDictionaryClone(type);
                visited[value] = clone;
                foreach (DictionaryEntry entry in dictionary)
                {
                    string childPath = string.IsNullOrWhiteSpace(path) ? Convert.ToString(entry.Key) ?? string.Empty : $"{path}.{entry.Key}";
                    clone[entry.Key] = NormalizeValue(entry.Value, childPath, options, visited);
                }

                return clone;
            }

            if (value is IEnumerable enumerable && value is not string)
            {
                Type elementType = GetEnumerableElementType(type);
                IList items = CreateListClone(type, elementType);
                visited[value] = items;
                foreach ((object? item, int index) in enumerable.Cast<object?>().Select((item, index) => (item, index)))
                {
                    items.Add(NormalizeValue(item, $"{path}[{index}]", options, visited));
                }

                if (ShouldIgnoreCollectionOrder(options))
                {
                    List<object?> sortedItems = items.Cast<object?>().OrderBy(CreateSortKey, StringComparer.Ordinal).ToList();
                    items.Clear();
                    foreach (object? item in sortedItems)
                    {
                        items.Add(item);
                    }
                }

                return ConvertToOriginalCollectionType(type, elementType, items);
            }

            object? cloneObject = CreateObjectClone(type);
            if (cloneObject is null)
            {
                return value;
            }

            visited[value] = cloneObject;
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.GetIndexParameters().Length > 0 || !property.CanRead || !property.CanWrite)
                {
                    continue;
                }

                string propertyPath = string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}";
                object? normalizedPropertyValue = HasJsonIgnoreAttribute(property) || ShouldIgnoreByRule(propertyPath, options) || ShouldIgnoreBySmartPath(propertyPath, options)
                    ? GetDefaultValue(property.PropertyType)
                    : NormalizeValue(property.GetValue(value), propertyPath, options, visited);

                property.SetValue(cloneObject, normalizedPropertyValue);
            }

            return cloneObject;
        }

        private static bool ShouldIgnoreBySmartPath(string path, ComparisonOptions options)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            string leafName = GetLeafPropertyName(path);
            foreach (SmartIgnoreRuleDefinition rule in options.SmartIgnoreRules.Where(rule => rule.IsEnabled))
            {
                if (rule.Kind == SmartIgnoreRuleKind.PropertyName
                    && string.Equals(leafName, rule.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                if (rule.Kind == SmartIgnoreRuleKind.NamePattern && MatchesPattern(path, rule.Value))
                {
                    return true;
                }
            }

            return false;
        }

        public static bool HasJsonIgnoreAttribute(PropertyInfo property) =>
            property.GetCustomAttribute<JsonIgnoreAttribute>() is not null
            || property.GetCustomAttributes(inherit: true).Any(attribute =>
                string.Equals(attribute.GetType().FullName, "Newtonsoft.Json.JsonIgnoreAttribute", StringComparison.Ordinal));

        private static object? CreateObjectClone(Type type)
        {
            ConstructorInfo? constructor = type.GetConstructor(Type.EmptyTypes);
            return constructor is null ? null : Activator.CreateInstance(type);
        }

        private static IList CreateListClone(Type sourceType, Type elementType)
        {
            if (!sourceType.IsInterface && sourceType.GetConstructor(Type.EmptyTypes) is not null && typeof(IList).IsAssignableFrom(sourceType))
            {
                return (IList)Activator.CreateInstance(sourceType)!;
            }

            Type listType = typeof(List<>).MakeGenericType(elementType);
            return (IList)Activator.CreateInstance(listType)!;
        }

        /// <summary>
        /// Collection types that aren't IList-constructible (HashSet, Queue, Stack, ImmutableList, etc.)
        /// get built up as a temporary List during normalization/sorting. Convert back to the original
        /// declared type here so reflection-based property assignment doesn't throw.
        /// </summary>
        private static object ConvertToOriginalCollectionType(Type sourceType, Type elementType, IList items)
        {
            if (sourceType.IsInstanceOfType(items))
            {
                return items;
            }

            if (!sourceType.IsInterface)
            {
                ConstructorInfo? enumerableConstructor = sourceType.GetConstructor(
                    new[] { typeof(IEnumerable<>).MakeGenericType(elementType) });
                if (enumerableConstructor is not null)
                {
                    return enumerableConstructor.Invoke(new object?[] { items })!;
                }
            }

            return items;
        }

        private static IDictionary CreateDictionaryClone(Type sourceType)
        {
            if (!sourceType.IsInterface && sourceType.GetConstructor(Type.EmptyTypes) is not null && typeof(IDictionary).IsAssignableFrom(sourceType))
            {
                return (IDictionary)Activator.CreateInstance(sourceType)!;
            }

            return new Hashtable();
        }

        private static Type GetEnumerableElementType(Type type)
        {
            if (type.IsGenericType && type.GetGenericArguments().Length == 1)
            {
                return type.GetGenericArguments()[0];
            }

            Type? enumerableType = type.GetInterfaces()
                .FirstOrDefault(interfaceType => interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            return enumerableType?.GetGenericArguments()[0] ?? typeof(object);
        }

        private static string CreateSortKey(object? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            try
            {
                return JsonSerializer.Serialize(value, value.GetType());
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                return value.ToString() ?? string.Empty;
            }
        }

        public static object? GetDefaultValue(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        public static bool IsSimpleValue(Type type) =>
            type.IsPrimitive
            || type.IsEnum
            || type == typeof(string)
            || type == typeof(decimal)
            || type == typeof(DateTime)
            || type == typeof(DateTimeOffset)
            || type == typeof(TimeSpan)
            || type == typeof(Guid)
            || type == typeof(Uri);
    }
}



