using System.Collections;
using System.Collections.Concurrent;
using System.Buffers;
using System.Diagnostics;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
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
            () => ComparisonModelPreparation.Create(modelA, modelB, options, rules));
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

        public bool ShouldIgnorePath(string path) => ignoredPaths.Any(matcher => matcher.IsMatch(path));

        public bool ShouldIgnoreChild(string parentPath, string propertyName) => ignoredPaths.Any(matcher => matcher.IsDirectChildMatch(parentPath, propertyName));

        public bool ShouldIgnoreSmartPropertyName(string propertyName) => smartPropertyNames.Contains(propertyName);

        public bool ShouldTreatNullAndEmptyAsEqual(string path) => nullEmptyPaths.Any(matcher => matcher.IsMatch(path));

        public bool ShouldIgnoreCollectionElement(string parentPath, int index) => ignoredPaths.Any(matcher => matcher.IsCollectionElementMatch(parentPath, index));

        public bool ShouldIgnoreSmartPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
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
                if (pattern.IsMatch(path))
                {
                    return true;
                }
            }

            return false;
        }

        private static ReadOnlySpan<char> GetPreparedLeafPropertyName(string path)
        {
            ReadOnlySpan<char> leaf = path.AsSpan(path.LastIndexOf('.') + 1);
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

        public bool IsMatch(string candidate)
        {
            if (hasCollectionWildcard)
            {
                return MatchesCollectionWildcard(path, candidate);
            }

            return string.Equals(path, candidate, StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(path + ".", StringComparison.OrdinalIgnoreCase)
                || candidate.StartsWith(path + "[", StringComparison.OrdinalIgnoreCase)
                || wildcard?.IsMatch(candidate) == true;
        }

        public bool IsDirectChildMatch(string parentPath, string propertyName)
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

        public bool IsCollectionElementMatch(string parentPath, int index)
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
        private readonly List<ModelMutation> mutations = new();

        private ComparisonModelPreparation()
        {
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

            Dictionary<object, object> visitedA = new(ReferenceEqualityComparer.Instance);
            Dictionary<object, object> visitedB = new(ReferenceEqualityComparer.Instance);
            ModelA = PrepareValue(modelA, string.Empty, options, rules, visitedA) ?? modelA;
            ModelB = PrepareValue(modelB, string.Empty, options, rules, visitedB) ?? modelB;
        }

        public object ModelA { get; private set; }

        public object ModelB { get; private set; }

        public static ComparisonModelPreparation Create(object modelA, object modelB, ComparisonOptions options, PreparedComparisonRules rules)
        {
            ComparisonModelPreparation preparation = new();
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
            for (int index = mutations.Count - 1; index >= 0; index--)
            {
                mutations[index].Restore();
            }

            mutations.Clear();
        }

        private object? PrepareValue(
            object? value,
            string path,
            ComparisonOptions options,
            PreparedComparisonRules rules,
            Dictionary<object, object> visited)
        {
            if (value is null)
            {
                return value;
            }

            if (rules.ShouldIgnorePath(path) || rules.ShouldIgnoreSmartPath(path))
            {
                return LegacyComparisonModelNormalizer.GetDefaultValue(value.GetType());
            }

            if (LegacyComparisonModelNormalizer.IsSimpleValue(value.GetType()))
            {
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
                object clone = LegacyComparisonModelNormalizer.NormalizeBranch(value, path, options);
                visited[value] = clone;
                return clone;
            }

            TypePreparationPlan typePlan = TypePlans.GetOrAdd(type, static candidate => TypePreparationPlan.Create(candidate));
            if (!typePlan.CanMutateInPlace)
            {
                object clone = LegacyComparisonModelNormalizer.NormalizeBranch(value, path, options);
                visited[value] = clone;
                return clone;
            }

            visited[value] = value;
            foreach (PreparedProperty property in typePlan.Properties)
            {
                object? original = property.Get(value);
                object? prepared = property.IsJsonIgnored
                    || rules.ShouldIgnoreSmartPropertyName(property.Name)
                    || rules.ShouldIgnoreChild(path, property.Name)
                    ? LegacyComparisonModelNormalizer.GetDefaultValue(property.PropertyType)
                    : PrepareValue(
                        original,
                        string.IsNullOrWhiteSpace(path) ? property.Name : $"{path}.{property.Name}",
                        options,
                        rules,
                        visited);

                if (!ReferenceEquals(original, prepared) && !Equals(original, prepared))
                {
                    mutations.Add(ModelMutation.ForProperty(value, property, original));
                    property.Set(value, prepared);
                }
            }

            return value;
        }

        private object PrepareArray(Array array, string path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            object?[] snapshot = array.Cast<object?>().ToArray();
            mutations.Add(ModelMutation.ForArray(array, snapshot));
            visited[array] = array;
            object?[] prepared = new object?[snapshot.Length];
            for (int index = 0; index < snapshot.Length; index++)
            {
                prepared[index] = PrepareCollectionItem(snapshot[index], path, index, options, rules, visited);
            }

            if (rules.IgnoreCollectionOrder)
            {
                SortPreparedItems(prepared);
            }

            for (int index = 0; index < prepared.Length; index++)
            {
                array.SetValue(prepared[index], index);
            }

            return array;
        }

        private object PrepareList(IList list, string path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            object?[] snapshot = list.Cast<object?>().ToArray();
            mutations.Add(ModelMutation.ForList(list, snapshot));
            visited[list] = list;
            object?[] prepared = new object?[snapshot.Length];
            for (int index = 0; index < snapshot.Length; index++)
            {
                prepared[index] = PrepareCollectionItem(snapshot[index], path, index, options, rules, visited);
            }

            if (rules.IgnoreCollectionOrder)
            {
                SortPreparedItems(prepared);
            }

            for (int index = 0; index < prepared.Length; index++)
            {
                list[index] = prepared[index];
            }

            return list;
        }

        private object? PrepareCollectionItem(
            object? item,
            string parentPath,
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
                return rules.ShouldIgnoreCollectionElement(parentPath, index)
                    ? LegacyComparisonModelNormalizer.GetDefaultValue(itemType)
                    : item;
            }

            return PrepareValue(item, $"{parentPath}[{index}]", options, rules, visited);
        }

        private object PrepareDictionary(IDictionary dictionary, string path, ComparisonOptions options, PreparedComparisonRules rules, Dictionary<object, object> visited)
        {
            List<DictionaryEntry> entries = new(dictionary.Count);
            foreach (DictionaryEntry entry in dictionary)
            {
                entries.Add(entry);
            }

            DictionaryEntry[] snapshot = entries.ToArray();
            mutations.Add(ModelMutation.ForDictionary(dictionary, snapshot));
            visited[dictionary] = dictionary;
            foreach (DictionaryEntry entry in snapshot)
            {
                string childName = Convert.ToString(entry.Key) ?? string.Empty;
                object? entryValue = entry.Value;
                if (entryValue is not null
                    && LegacyComparisonModelNormalizer.IsSimpleValue(entryValue.GetType())
                    && !rules.ShouldIgnoreChild(path, childName)
                    && !rules.ShouldIgnoreSmartPropertyName(childName))
                {
                    continue;
                }

                string childPath = string.IsNullOrWhiteSpace(path) ? childName : $"{path}.{childName}";
                dictionary[entry.Key] = PrepareValue(entryValue, childPath, options, rules, visited);
            }

            return dictionary;
        }

        private static void SortPreparedItems(object?[] items)
        {
            if (items.Length < 2)
            {
                return;
            }

            try
            {
                object?[]? primaryValues = TryGetPrimarySortValues(items);
                using PooledSortKeyBatch keys = PooledSortKeyBatch.Create(primaryValues ?? items);
                keys.SortInto(items, resolveCollisionsWith: primaryValues is null ? null : (object?[])items.Clone());
            }
            catch (Exception ex) when (ex is NotSupportedException or JsonException or InvalidOperationException)
            {
                using PooledSortKeySet keys = new(items);
                object?[] original = (object?[])items.Clone();
                int[] order = Enumerable.Range(0, items.Length).ToArray();
                Array.Sort(order, (left, right) =>
                {
                    int comparison = PooledSortKeyComparer.Instance.Compare(keys.Items[left], keys.Items[right]);
                    return comparison != 0 ? comparison : left.CompareTo(right);
                });
                for (int index = 0; index < order.Length; index++) { items[index] = original[order[index]]; }
            }
        }

        private static object?[]? TryGetPrimarySortValues(object?[] items)
        {
            Type? itemType = items[0]?.GetType();
            if (itemType is null
                || LegacyComparisonModelNormalizer.IsSimpleValue(itemType)
                || typeof(IEnumerable).IsAssignableFrom(itemType)
                || !AcyclicSortTypeCache.GetOrAdd(itemType, static type => IsStaticallyAcyclic(type, new HashSet<Type>(), new HashSet<Type>()))
                || items.Any(item => item?.GetType() != itemType))
            {
                return null;
            }

            SortPropertyPlan[] properties = SortPropertyPlans.GetOrAdd(itemType, CreateSortPropertyPlans);
            if (properties.Length == 0)
            {
                return null;
            }

            object?[] primaryValues = new object?[items.Length];
            for (int index = 0; index < items.Length; index++)
            {
                primaryValues[index] = properties[0].Get(items[index]!);
            }

            return primaryValues;
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
    }

    private sealed class PooledSortKeyBatch : IDisposable
    {
        private const int MaximumInitialBufferLength = 64 * 1024;
        private readonly PooledByteBufferWriter output;
        private readonly BatchedSortEntry[] entries;

        private PooledSortKeyBatch(PooledByteBufferWriter output, BatchedSortEntry[] entries)
        {
            this.output = output;
            this.entries = entries;
        }

        public static PooledSortKeyBatch Create(object?[] values)
        {
            int initialCapacity = Math.Min(
                MaximumInitialBufferLength,
                Math.Max(256, values.Length > MaximumInitialBufferLength / 256
                    ? MaximumInitialBufferLength
                    : values.Length * 256));
            PooledByteBufferWriter output = new(initialCapacity);
            try
            {
                BatchedSortEntry[] entries = new BatchedSortEntry[values.Length];
                HashSet<object> visited = new(ReferenceEqualityComparer.Instance);
                using Utf8JsonWriter writer = new(output);
                for (int index = 0; index < values.Length; index++)
                {
                    int offset = output.WrittenCount;
                    visited.Clear();
                    WriteSortValue(writer, values[index], visited);
                    writer.Flush();

                    ReadOnlySpan<byte> bytes = output.WrittenSpan[offset..];
                    bool isAscii = bytes.IndexOfAnyExceptInRange((byte)0, (byte)127) < 0;
                    entries[index] = new BatchedSortEntry(
                        values[index],
                        offset,
                        bytes.Length,
                        isAscii ? null : Encoding.UTF8.GetString(bytes),
                        index);
                    if (index + 1 < values.Length)
                    {
                        writer.Reset(output);
                    }
                }

                return new PooledSortKeyBatch(output, entries);
            }
            catch
            {
                output.Dispose();
                throw;
            }
        }

        public void SortInto(object?[] destination, object?[]? resolveCollisionsWith = null)
        {
            BatchedSortEntryComparer comparer = new(output);
            Array.Sort(entries, comparer);
            if (resolveCollisionsWith is not null)
            {
                int start = 0;
                while (start < entries.Length)
                {
                    int end = start + 1;
                    while (end < entries.Length && comparer.CompareKey(entries[start], entries[end]) == 0) { end++; }
                    if (end - start > 1)
                    {
                        object?[] collisionValues = new object?[end - start];
                        for (int index = start; index < end; index++)
                        {
                            collisionValues[index - start] = resolveCollisionsWith[entries[index].OriginalIndex];
                        }

                        using PooledSortKeyBatch fullKeys = Create(collisionValues);
                        fullKeys.SortInto(collisionValues);
                        for (int index = start; index < end; index++)
                        {
                            entries[index] = entries[index] with { Value = collisionValues[index - start] };
                        }
                    }

                    start = end;
                }
            }

            for (int index = 0; index < entries.Length; index++)
            {
                destination[index] = resolveCollisionsWith is null
                    ? entries[index].Value
                    : resolveCollisionsWith[entries[index].OriginalIndex];
            }

            if (resolveCollisionsWith is not null)
            {
                // Collision groups already carry fully sorted original values.
                int start = 0;
                while (start < entries.Length)
                {
                    int end = start + 1;
                    while (end < entries.Length && comparer.CompareKey(entries[start], entries[end]) == 0) { end++; }
                    if (end - start > 1)
                    {
                        for (int index = start; index < end; index++) { destination[index] = entries[index].Value; }
                    }
                    start = end;
                }
            }
        }

        public void Dispose() => output.Dispose();
    }

    private readonly record struct BatchedSortEntry(
        object? Value,
        int Offset,
        int Length,
        string? DecodedKey,
        int OriginalIndex);

    private sealed class BatchedSortEntryComparer(PooledByteBufferWriter output) : IComparer<BatchedSortEntry>
    {
        public int Compare(BatchedSortEntry x, BatchedSortEntry y)
        {
            int keyComparison = CompareKey(x, y);
            return keyComparison != 0 ? keyComparison : x.OriginalIndex.CompareTo(y.OriginalIndex);
        }

        public int CompareKey(BatchedSortEntry x, BatchedSortEntry y) =>
            x.DecodedKey is null && y.DecodedKey is null
                ? output.WrittenSpan.Slice(x.Offset, x.Length)
                    .SequenceCompareTo(output.WrittenSpan.Slice(y.Offset, y.Length))
                : string.CompareOrdinal(
                    x.DecodedKey ?? Encoding.UTF8.GetString(output.WrittenSpan.Slice(x.Offset, x.Length)),
                    y.DecodedKey ?? Encoding.UTF8.GetString(output.WrittenSpan.Slice(y.Offset, y.Length)));
    }

    private sealed record TypePreparationPlan(bool CanMutateInPlace, PreparedProperty[] Properties)
    {
        public static TypePreparationPlan Create(Type type)
        {
            PropertyInfo[] readable = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0 && property.CanRead)
                .ToArray();
            bool canMutate = readable.All(property => property.CanWrite);
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
        public static ModelMutation ForArray(Array target, object?[] original) => new(target, null, original);
        public static ModelMutation ForList(IList target, object?[] original) => new(target, null, original);
        public static ModelMutation ForDictionary(IDictionary target, DictionaryEntry[] original) => new(target, null, original);

        public void Restore()
        {
            if (property is not null)
            {
                property.Set(target, original);
                return;
            }

            if (target is Array array && original is object?[] arrayItems)
            {
                for (int index = 0; index < arrayItems.Length; index++) { array.SetValue(arrayItems[index], index); }
            }
            else if (target is IList list && original is object?[] listItems)
            {
                for (int index = 0; index < listItems.Length; index++) { list[index] = listItems[index]; }
            }
            else if (target is IDictionary dictionary && original is DictionaryEntry[] entries)
            {
                dictionary.Clear();
                foreach (DictionaryEntry entry in entries) { dictionary[entry.Key] = entry.Value; }
            }
        }
    }

    private sealed class PooledSortKeySet : IDisposable
    {
        public PooledSortKeySet(object?[] values) => Items = values.Select(PooledSortKey.Create).ToArray();
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
            try
            {
                using Utf8JsonWriter writer = new(output);
                WriteSortValue(writer, value, new HashSet<object>(ReferenceEqualityComparer.Instance));
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



