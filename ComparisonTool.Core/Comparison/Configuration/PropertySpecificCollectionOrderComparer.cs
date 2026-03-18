namespace ComparisonTool.Core.Comparison.Configuration;

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// A custom comparer that ignores collection order for specified properties only
/// Performance optimized - removed expensive debug logging.
/// </summary>
public class PropertySpecificCollectionOrderComparer : BaseTypeComparer
{
    private static readonly string[] PreferredIdentifierPropertyNames =
    {
        "Id",
        "ID",
        "ItemId",
        "OrderId",
        "ProductId",
        "LineId",
        "ReferenceId",
        "CustomerId",
        "TransactionId",
        "Name",
        "Key",
        "Code",
        "SKU",
        "Sku",
        "Identifier",
        "ExternalId",
        "Guid",
    };

    private static readonly ConcurrentDictionary<(Type Type, string PropertyName), PropertyInfo?> IdentifierPropertyCache = new();

    // Use simple thread-safe tracking without expensive concurrent dictionaries
    private static int comparisonCount = 0;

    private readonly HashSet<string> propertiesToIgnoreOrder;
    private readonly HashSet<string> ignoredPropertyPatterns;
    private readonly ILogger logger;
    private readonly bool applyGlobally;

    // Explicitly track if we have rules for Results or RelatedItems (for fast lookup)
    private readonly bool hasResultsRule;
    private readonly bool hasRelatedItemsRule;

    /// <summary>
    /// Initializes a new instance of the <see cref="PropertySpecificCollectionOrderComparer"/> class.
    /// Constructor that takes a list of property paths where order should be ignored.
    /// </summary>
    /// <param name="rootComparer">The root comparer.</param>
    /// <param name="propertiesToIgnoreOrder">Property paths where collection order should be ignored.</param>
    /// <param name="logger">Optional logger.</param>
    public PropertySpecificCollectionOrderComparer(
        RootComparer rootComparer,
        IEnumerable<string> propertiesToIgnoreOrder,
        ILogger? logger = null,
        bool applyGlobally = false,
        IEnumerable<string>? ignoredPropertyPatterns = null)
        : base(rootComparer)
    {
        this.propertiesToIgnoreOrder = new HashSet<string>(propertiesToIgnoreOrder ?? Enumerable.Empty<string>(), System.StringComparer.Ordinal);
        this.ignoredPropertyPatterns = new HashSet<string>(ignoredPropertyPatterns ?? Enumerable.Empty<string>(), System.StringComparer.OrdinalIgnoreCase);
        this.logger = logger ?? NullLogger.Instance;
        this.applyGlobally = applyGlobally;

        // Check if we have rules for specific collections using simple IndexOf
        hasResultsRule = this.propertiesToIgnoreOrder.Any(p =>
            p.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);

        hasRelatedItemsRule = this.propertiesToIgnoreOrder.Any(p =>
            p.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);

        // Simplified logging - only log the essential information
        this.logger.LogDebug(
            "PropertySpecificCollectionOrderComparer initialized with {Count} properties, global mode: {ApplyGlobally}, Results rule: {HasResults}, RelatedItems rule: {HasRelatedItems}",
            this.propertiesToIgnoreOrder.Count,
            this.applyGlobally,
            hasResultsRule,
            hasRelatedItemsRule);
    }

    /// <summary>
    /// Get performance statistics for monitoring.
    /// </summary>
    /// <returns>Number of comparisons performed.</returns>
    public static int GetComparisonCount() => comparisonCount;

    /// <summary>
    /// Check if this comparer handles the specified types.
    /// </summary>
    /// <returns></returns>
    public override bool IsTypeMatch(Type type1, Type type2)
    {
        var isEnumerable1 = type1 != null && typeof(IEnumerable).IsAssignableFrom(type1);
        var isEnumerable2 = type2 != null && typeof(IEnumerable).IsAssignableFrom(type2);
        var isString1 = type1 == typeof(string);
        var isString2 = type2 == typeof(string);

        // Make sure this only handles collections, not strings or other non-collections
        return isEnumerable1 && isEnumerable2 && !isString1 && !isString2;
    }

    /// <summary>
    /// Compare the collections, ignoring order for specified properties.
    /// </summary>
    public override void CompareType(CompareParms parms)
    {
        // Increment comparison counter for essential tracking only
        var currentCount = System.Threading.Interlocked.Increment(ref comparisonCount);

        // Minimal logging - only for first few comparisons or if debug enabled
        if (currentCount <= 3 || logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Collection comparison #{Count} at path: '{Path}'", currentCount, parms.BreadCrumb ?? "unknown");
        }

        // Early termination if we've already hit MaxDifferences
        if (parms.Result.Differences.Count >= parms.Config.MaxDifferences)
        {
            return;
        }

        // Save original config state - we'll restore this at the end
        var originalIgnoreCollectionOrder = parms.Config.IgnoreCollectionOrder;

        try
        {
            var shouldIgnoreOrder = applyGlobally || ShouldIgnoreOrderForPath(parms.BreadCrumb);

            if (!shouldIgnoreOrder)
            {
                CompareWithCollectionComparer(parms, ignoreCollectionOrder: false);
                return;
            }

            if (TryCompareUsingDeterministicOrdering(parms))
            {
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Applied deterministic ordering optimization for '{Path}'", parms.BreadCrumb ?? "<root>");
                }
                return;
            }

            logger.LogWarning(
                "Deterministic ordering failed for collection at '{Path}' — falling back to O(n²) comparison. This may be slow for large collections.",
                parms.BreadCrumb ?? "<root>");
            CompareWithCollectionComparer(parms, ignoreCollectionOrder: true);
        }
        finally
        {
            // Always restore original setting
            parms.Config.IgnoreCollectionOrder = originalIgnoreCollectionOrder;
        }
    }

    private void CompareWithCollectionComparer(CompareParms parms, bool ignoreCollectionOrder)
    {
        parms.Config.IgnoreCollectionOrder = ignoreCollectionOrder;
        var collectionComparer = new CollectionComparer(RootComparer);
        collectionComparer.CompareType(parms);
    }

    private bool ShouldIgnoreOrderForPath(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var isResultsCollection = hasResultsRule &&
            (path.IndexOf("Results", StringComparison.OrdinalIgnoreCase) >= 0);
        var isRelatedItemsCollection = hasRelatedItemsRule &&
            (path.IndexOf("RelatedItems", StringComparison.OrdinalIgnoreCase) >= 0);

        if (isResultsCollection || isRelatedItemsCollection)
        {
            if (logger.IsEnabled(LogLevel.Debug))
            {
                logger.LogDebug("Fast path match for '{Path}' - ignoring collection order", path);
            }

            return true;
        }

        var shouldIgnoreOrder = propertiesToIgnoreOrder.Any(pattern => DoesPathMatchPattern(path, pattern));

        if (shouldIgnoreOrder && logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Pattern match for '{Path}' - ignoring collection order", path);
        }

        return shouldIgnoreOrder;
    }

    private bool TryCompareUsingDeterministicOrdering(CompareParms parms)
    {
        if (parms.Object1 is not IEnumerable enumerable1 || parms.Object2 is not IEnumerable enumerable2)
        {
            return false;
        }

        var items1 = MaterializeItems(enumerable1);
        var items2 = MaterializeItems(enumerable2);

        if (!TryCreateOrderedCollections(parms.BreadCrumb, items1, items2, out var orderedItems1, out var orderedItems2))
        {
            return false;
        }

        parms.Config.IgnoreCollectionOrder = false;
        var orderedParms = CreateOrderedCompareParms(parms, orderedItems1, orderedItems2);
        var collectionComparer = new CollectionComparer(RootComparer);
        collectionComparer.CompareType(orderedParms);

        return true;
    }

    private static List<object?> MaterializeItems(IEnumerable enumerable)
    {
        var items = new List<object?>();
        foreach (var item in enumerable)
        {
            items.Add(item);
        }

        return items;
    }

    private bool TryCreateOrderedCollections(
        string? collectionPath,
        IReadOnlyList<object?> items1,
        IReadOnlyList<object?> items2,
        out object?[] orderedItems1,
        out object?[] orderedItems2)
    {
        if (TryCreateOrderedScalarCollections(items1, items2, out orderedItems1, out orderedItems2))
        {
            return true;
        }

        return TryCreateOrderedObjectCollections(collectionPath, items1, items2, out orderedItems1, out orderedItems2);
    }

    private bool TryCreateOrderedScalarCollections(
        IReadOnlyList<object?> items1,
        IReadOnlyList<object?> items2,
        out object?[] orderedItems1,
        out object?[] orderedItems2)
    {
        if (!TryBuildScalarEntries(items1, out var entries1) ||
            !TryBuildScalarEntries(items2, out var entries2))
        {
            orderedItems1 = Array.Empty<object?>();
            orderedItems2 = Array.Empty<object?>();
            return false;
        }

        orderedItems1 = OrderEntries(entries1);
        orderedItems2 = OrderEntries(entries2);
        return true;
    }

    private bool TryBuildScalarEntries(IReadOnlyList<object?> items, out List<OrderedCollectionEntry> entries)
    {
        entries = new List<OrderedCollectionEntry>(items.Count);
        var keyCounts = new Dictionary<string, int>(System.StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item != null && !IsSimpleScalarType(item.GetType()))
            {
                entries.Clear();
                return false;
            }

            if (!TryCreateScalarKey(item, out var baseKey))
            {
                entries.Clear();
                return false;
            }

            // Handle duplicates by appending occurrence index
            if (keyCounts.TryGetValue(baseKey, out var count))
            {
                keyCounts[baseKey] = count + 1;
            }
            else
            {
                keyCounts[baseKey] = 0;
                count = 0;
            }

            var key = count == 0 ? baseKey : $"{baseKey}::{count}";
            entries.Add(new OrderedCollectionEntry(key, item));
        }

        return true;
    }

    private bool TryCreateOrderedObjectCollections(
        string? collectionPath,
        IReadOnlyList<object?> items1,
        IReadOnlyList<object?> items2,
        out object?[] orderedItems1,
        out object?[] orderedItems2)
    {
        foreach (var propertyName in GetCandidateIdentifierPropertyNames(collectionPath, items1, items2))
        {
            if (!TryBuildIdentifierEntries(items1, propertyName, out var entries1) ||
                !TryBuildIdentifierEntries(items2, propertyName, out var entries2))
            {
                continue;
            }

            orderedItems1 = OrderEntries(entries1);
            orderedItems2 = OrderEntries(entries2);
            return true;
        }

        // Composite key fallback: hash all scalar properties
        if (TryBuildCompositeKeyEntries(collectionPath, items1, out var compositeEntries1) &&
            TryBuildCompositeKeyEntries(collectionPath, items2, out var compositeEntries2))
        {
            orderedItems1 = OrderEntries(compositeEntries1);
            orderedItems2 = OrderEntries(compositeEntries2);
            return true;
        }

        orderedItems1 = Array.Empty<object?>();
        orderedItems2 = Array.Empty<object?>();
        return false;
    }

    private IEnumerable<string> GetCandidateIdentifierPropertyNames(
        string? collectionPath,
        IReadOnlyList<object?> items1,
        IReadOnlyList<object?> items2)
    {
        var yieldedNames = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var propertyName in PreferredIdentifierPropertyNames)
        {
            if (yieldedNames.Add(propertyName) && !IsIgnoredPropertyForCollection(collectionPath, propertyName))
            {
                yield return propertyName;
            }
        }

        foreach (var propertyName in GetSharedHeuristicIdentifierPropertyNames(items1, items2))
        {
            if (yieldedNames.Add(propertyName) && !IsIgnoredPropertyForCollection(collectionPath, propertyName))
            {
                yield return propertyName;
            }
        }
    }

    private static IEnumerable<string> GetSharedHeuristicIdentifierPropertyNames(
        IReadOnlyList<object?> items1,
        IReadOnlyList<object?> items2)
    {
        var leftNames = GetHeuristicIdentifierPropertyNames(items1);
        if (leftNames.Count == 0)
        {
            yield break;
        }

        var rightNames = GetHeuristicIdentifierPropertyNames(items2);
        if (rightNames.Count == 0)
        {
            yield break;
        }

        foreach (var propertyName in leftNames.Intersect(rightNames, System.StringComparer.OrdinalIgnoreCase))
        {
            yield return propertyName;
        }
    }

    private static HashSet<string> GetHeuristicIdentifierPropertyNames(IReadOnlyList<object?> items)
    {
        var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

        foreach (var item in items)
        {
            if (item == null || IsSimpleScalarType(item.GetType()))
            {
                return new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            }

            foreach (var property in item.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (!IsSimpleScalarType(property.PropertyType))
                {
                    continue;
                }

                if (LooksLikeIdentifierName(property.Name))
                {
                    names.Add(property.Name);
                }
            }
        }

        return names;
    }

    private static bool LooksLikeIdentifierName(string propertyName) =>
        propertyName.EndsWith("Id", StringComparison.OrdinalIgnoreCase) ||
        propertyName.EndsWith("Key", StringComparison.OrdinalIgnoreCase) ||
        propertyName.EndsWith("Code", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "SKU", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(propertyName, "Identifier", StringComparison.OrdinalIgnoreCase);

    private bool TryBuildIdentifierEntries(
        IReadOnlyList<object?> items,
        string propertyName,
        out List<OrderedCollectionEntry> entries)
    {
        entries = new List<OrderedCollectionEntry>(items.Count);
        var keys = new HashSet<string>(System.StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item == null || IsSimpleScalarType(item.GetType()))
            {
                entries.Clear();
                return false;
            }

            var identifierProperty = GetIdentifierProperty(item.GetType(), propertyName);
            if (identifierProperty == null)
            {
                entries.Clear();
                return false;
            }

            var identifierValue = identifierProperty.GetValue(item);
            if (!TryCreateScalarKey(identifierValue, out var key) || !keys.Add(key))
            {
                // Duplicate identifier — fail so composite key fallback handles this correctly
                entries.Clear();
                return false;
            }

            entries.Add(new OrderedCollectionEntry(key, item));
        }

        return true;
    }

    private static PropertyInfo? GetIdentifierProperty(Type type, string propertyName) =>
        IdentifierPropertyCache.GetOrAdd((type, propertyName), key => ResolveIdentifierProperty(key.Type, key.PropertyName));

    private static PropertyInfo? ResolveIdentifierProperty(Type type, string propertyName)
    {
        var property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public) ??
            type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(p => string.Equals(p.Name, propertyName, StringComparison.OrdinalIgnoreCase));

        if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
        {
            return null;
        }

        return IsSimpleScalarType(property.PropertyType) ? property : null;
    }

    /// <summary>
    /// Last-resort: build a sort key by hashing ALL public scalar properties of each item.
    /// This avoids the O(n²) fallback in CompareNetObjects.
    /// </summary>
    private bool TryBuildCompositeKeyEntries(
        string? collectionPath,
        IReadOnlyList<object?> items,
        out List<OrderedCollectionEntry> entries)
    {
        entries = new List<OrderedCollectionEntry>(items.Count);
        var keyCounts = new Dictionary<string, int>(System.StringComparer.Ordinal);

        foreach (var item in items)
        {
            if (item == null)
            {
                entries.Clear();
                return false;
            }

            var type = item.GetType();
            if (IsSimpleScalarType(type))
            {
                entries.Clear();
                return false;
            }

            var keyParts = new List<string>();
            foreach (var prop in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (!prop.CanRead || prop.GetIndexParameters().Length != 0)
                    continue;
                if (!IsSimpleScalarType(prop.PropertyType))
                    continue;
                if (IsIgnoredPropertyForCollection(collectionPath, prop.Name))
                    continue;

                var val = prop.GetValue(item);
                if (TryCreateScalarKey(val, out var partKey))
                {
                    keyParts.Add($"{prop.Name}={partKey}");
                }
            }

            if (keyParts.Count == 0)
            {
                entries.Clear();
                return false;
            }

            var baseKey = string.Join("|", keyParts);

            if (keyCounts.TryGetValue(baseKey, out var count))
            {
                keyCounts[baseKey] = count + 1;
            }
            else
            {
                keyCounts[baseKey] = 0;
                count = 0;
            }

            var key = count == 0 ? baseKey : $"{baseKey}::{count}";
            entries.Add(new OrderedCollectionEntry(key, item));
        }

        return entries.Count > 0;
    }

    private bool IsIgnoredPropertyForCollection(string? collectionPath, string propertyName)
    {
        if (ignoredPropertyPatterns.Count == 0)
        {
            return false;
        }

        foreach (var pattern in ignoredPropertyPatterns)
        {
            if (string.Equals(pattern, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            foreach (var candidatePath in GetCandidatePropertyPaths(collectionPath, propertyName))
            {
                if (string.Equals(candidatePath, pattern, StringComparison.OrdinalIgnoreCase) || DoesPathMatchPattern(candidatePath, pattern))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> GetCandidatePropertyPaths(string? collectionPath, string propertyName)
    {
        yield return propertyName;

        if (string.IsNullOrWhiteSpace(collectionPath))
        {
            yield break;
        }

        var normalizedPath = collectionPath.TrimEnd('.');
        yield return $"{normalizedPath}.{propertyName}";
        yield return $"{normalizedPath}[*].{propertyName}";
        yield return $"{normalizedPath}[0].{propertyName}";
        yield return $"{normalizedPath}[1].{propertyName}";
    }

    private static object?[] OrderEntries(IEnumerable<OrderedCollectionEntry> entries) =>
        entries
            .OrderBy(entry => entry.Key, System.StringComparer.Ordinal)
            .Select(entry => entry.Item)
            .ToArray();

    private static CompareParms CreateOrderedCompareParms(
        CompareParms originalParms,
        object?[] orderedItems1,
        object?[] orderedItems2) => new CompareParms
        {
            BreadCrumb = originalParms.BreadCrumb,
            Config = originalParms.Config,
            CustomPropertyComparer = originalParms.CustomPropertyComparer,
            Object1 = orderedItems1,
            Object2 = orderedItems2,
            Object1DeclaredType = orderedItems1.GetType(),
            Object2DeclaredType = orderedItems2.GetType(),
            Object1Type = orderedItems1.GetType(),
            Object2Type = orderedItems2.GetType(),
            ParentObject1 = originalParms.ParentObject1,
            ParentObject2 = originalParms.ParentObject2,
            Result = originalParms.Result,
        };

    private static bool TryCreateScalarKey(object? value, out string key)
    {
        if (value == null)
        {
            key = "<null>";
            return true;
        }

        var type = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (!IsSimpleScalarType(type))
        {
            key = string.Empty;
            return false;
        }

        key = type.FullName + ":" + ConvertScalarToInvariantString(value, type);
        return true;
    }

    private static string ConvertScalarToInvariantString(object value, Type type)
    {
        if (type == typeof(string))
        {
            return (string)value;
        }

        if (type == typeof(DateTime))
        {
            return ((DateTime)value).ToString("O", CultureInfo.InvariantCulture);
        }

        if (type == typeof(DateTimeOffset))
        {
            return ((DateTimeOffset)value).ToString("O", CultureInfo.InvariantCulture);
        }

        if (type == typeof(TimeSpan))
        {
            return ((TimeSpan)value).ToString("c", CultureInfo.InvariantCulture);
        }

        if (type == typeof(Guid))
        {
            return ((Guid)value).ToString("D");
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString() ?? string.Empty;
    }

    private static bool IsSimpleScalarType(Type type)
    {
        var actualType = Nullable.GetUnderlyingType(type) ?? type;

        return actualType.IsPrimitive ||
            actualType.IsEnum ||
            actualType == typeof(string) ||
            actualType == typeof(decimal) ||
            actualType == typeof(Guid) ||
            actualType == typeof(DateTime) ||
            actualType == typeof(DateTimeOffset) ||
            actualType == typeof(TimeSpan);
    }

    /// <summary>
    /// Optimized method to check if a path matches a pattern.
    /// </summary>
    private bool DoesPathMatchPattern(string path, string pattern)
    {
        // Fast exact match check
        if (string.Equals(path, pattern, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Fast prefix check for sub-properties
        if (path.StartsWith(pattern + ".", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Handle wildcard patterns [*] - convert to simple pattern matching
        if (pattern.Contains("[*]"))
        {
            // Simple approach: replace [*] with a regex and check
            try
            {
                var regexPattern = pattern.Replace("[*]", @"\[\d+\]");
                return System.Text.RegularExpressions.Regex.IsMatch(
                    path,
                    regexPattern,
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch
            {
                // Fallback to simple contains check if regex fails
                var basePattern = pattern.Replace("[*]", string.Empty);
                return path.Contains(basePattern, StringComparison.OrdinalIgnoreCase);
            }
        }

        return false;
    }

    private readonly struct OrderedCollectionEntry
    {
        public OrderedCollectionEntry(string key, object? item)
        {
            Key = key;
            Item = item;
        }

        public string Key
        {
            get;
        }

        public object? Item
        {
            get;
        }
    }
}
