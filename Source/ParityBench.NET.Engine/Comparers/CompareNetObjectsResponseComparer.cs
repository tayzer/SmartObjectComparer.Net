using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
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
    private readonly IRunArtifactStore artifactStore;
    private readonly IResponseBodyDeserializer deserializer;
    // CompareLogic owns reflection/type metadata caches. Keep one instance per
    // worker thread for the current options reference; constructing it per pair
    // defeats Caching=true and adds avoidable high-volume allocation.
    private readonly ThreadLocal<CompareLogicState?> compareLogicCache = new();

    public CompareNetObjectsResponseComparer(
        IRunArtifactStore artifactStore,
        IResponseBodyDeserializer deserializer)
    {
        this.artifactStore = artifactStore;
        this.deserializer = deserializer;
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

            IReadOnlyList<ComparisonDifference> differences = CompareModels(
                modelA,
                modelB,
                options.Comparison,
                GetCompareLogic(options.Comparison),
                timing);

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
        (object comparisonModelA, object comparisonModelB) normalized = Time(timing, static (c, e) => c.AddNormalization(e), () => (ComparisonModelNormalizer.Normalize(modelA, options), ComparisonModelNormalizer.Normalize(modelB, options)));
        ComparisonResult comparisonResult = Time(timing, static (c, e) => c.AddCompareNetObjects(e), () => compareLogic.Compare(normalized.comparisonModelA, normalized.comparisonModelB));
        return Time(timing, static (c, e) => c.AddMaterialization(e), () => MaterializeDifferences(comparisonResult.Differences, options));
    }

    private static T Time<T>(DetailedCompareMetricsCollector? timing, Action<DetailedCompareMetricsCollector, TimeSpan> record, Func<T> action)
    {
        if (timing is null) { return action(); }
        Stopwatch stopwatch = Stopwatch.StartNew();
        T result = action();
        stopwatch.Stop();
        record(timing, stopwatch.Elapsed);
        return result;
    }

    private static async Task<T> TimeAsync<T>(DetailedCompareMetricsCollector? timing, Action<DetailedCompareMetricsCollector, TimeSpan> record, Func<Task<T>> action)
    {
        if (timing is null) { return await action().ConfigureAwait(false); }
        Stopwatch stopwatch = Stopwatch.StartNew();
        T result = await action().ConfigureAwait(false);
        stopwatch.Stop();
        record(timing, stopwatch.Elapsed);
        return result;
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

    private static bool ShouldFilterDifference(Difference difference, ComparisonOptions options) =>
        ShouldIgnoreByRule(NormalizeComparisonPath(difference.PropertyName), options)
        || ShouldIgnoreBySmartRule(difference, options)
        || ShouldIgnoreByTrailingWhitespace(difference, options)
        || ShouldIgnoreByNullEmptyCollectionRule(difference, options);

    private static bool ShouldIgnoreByRule(string propertyPath, ComparisonOptions options) =>
        options.IgnoreRules
            .Where(rule => rule.IgnoreCompletely)
            .Any(rule => PathMatches(rule.PropertyPath, propertyPath));

    private static bool ShouldIgnoreBySmartRule(Difference difference, ComparisonOptions options)
    {
        string propertyPath = NormalizeComparisonPath(difference.PropertyName);
        string leafName = GetLeafPropertyName(propertyPath);

        foreach (SmartIgnoreRuleDefinition rule in options.SmartIgnoreRules.Where(rule => rule.IsEnabled))
        {
            if (rule.Kind == SmartIgnoreRuleKind.PropertyName
                && string.Equals(leafName, rule.Value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (rule.Kind == SmartIgnoreRuleKind.NamePattern && MatchesPattern(propertyPath, rule.Value))
            {
                return true;
            }

            if (rule.Kind == SmartIgnoreRuleKind.PropertyType && MatchesPropertyType(difference, rule.Value))
            {
                return true;
            }
        }

        return false;
    }

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

    private static bool ShouldIgnoreByNullEmptyCollectionRule(Difference difference, ComparisonOptions options)
    {
        bool appliesGlobally = options.TreatNullAndEmptyCollectionsAsEqual;
        bool appliesByRule = options.IgnoreRules.Any(rule =>
            rule.TreatNullAndEmptyCollectionsAsEqual && PathMatches(rule.PropertyPath, NormalizeComparisonPath(difference.PropertyName)));

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

    private static bool MatchesPropertyType(Difference difference, string typeName)
    {
        object? value = difference.Object1Value ?? difference.Object2Value;
        Type? type = value?.GetType();

        return type is not null
            && (string.Equals(type.Name, typeName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase));
    }

    private static string GetLeafPropertyName(string propertyPath)
    {
        string cleanedPath = Regex.Replace(NormalizeComparisonPath(propertyPath), "\\[\\d+\\]", string.Empty, RegexOptions.None, RegexTimeout);
        int separatorIndex = cleanedPath.LastIndexOf('.');
        return separatorIndex < 0 ? cleanedPath : cleanedPath[(separatorIndex + 1)..];
    }

    private static IReadOnlyList<ComparisonDifference> MaterializeDifferences(
        IEnumerable<Difference> differences,
        ComparisonOptions options)
    {
        HashSet<(string Path, string? ValueA, string? ValueB)> seen = new();
        List<ComparisonDifference> materialized = new();
        int limit = options.IncludeAllDifferences ? int.MaxValue : options.MaxDifferences;

        foreach (Difference difference in differences)
        {
            if (ShouldFilterDifference(difference, options))
            {
                continue;
            }

            string path = GetDomainDifferencePropertyPath(difference);
            string? valueA = difference.Object1Value?.ToString();
            string? valueB = difference.Object2Value?.ToString();
            if (!seen.Add((path.ToUpperInvariant(), valueA, valueB)))
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

    private static List<Difference> DeduplicateDifferences(IEnumerable<Difference> differences)
    {
        HashSet<(string Path, string? ValueA, string? ValueB)> seen =
            new HashSet<(string Path, string? ValueA, string? ValueB)>();
        List<Difference> uniqueDifferences = new List<Difference>();

        foreach (Difference difference in differences)
        {
            string propertyPath = GetDomainDifferencePropertyPath(difference);
            var dedupeKey = (propertyPath.ToUpperInvariant(), difference.Object1Value?.ToString(), difference.Object2Value?.ToString());

            if (seen.Add(dedupeKey))
            {
                uniqueDifferences.Add(difference);
            }
        }

        return uniqueDifferences;
    }

    private static ComparisonDifference ToDomainDifference(Difference difference) =>
        new ComparisonDifference(
            GetDomainDifferencePropertyPath(difference),
            difference.Object1Value?.ToString(),
            difference.Object2Value?.ToString(),
            difference.ToString());

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

    private static class ComparisonModelNormalizer
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

        private static bool HasJsonIgnoreAttribute(PropertyInfo property) =>
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

        private static object? GetDefaultValue(Type type) => type.IsValueType ? Activator.CreateInstance(type) : null;

        private static bool IsSimpleValue(Type type) =>
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



