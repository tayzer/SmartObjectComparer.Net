namespace ComparisonTool.Core.Comparison.Configuration;

using System;
using System.Linq;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

/// <summary>
/// Represents a rule for ignoring or configuring comparison for a specific property.
/// </summary>
public class IgnoreRule
{
    // NOTE: The logger field is problematic for simple JSON serialization/deserialization
    // as ILogger cannot be easily represented in JSON.
    // We use NullLogger.Instance as a fallback.
    [JsonIgnore] // Tell serializer to ignore this field completely
    private readonly ILogger logger;

    // Parameterless constructor explicitly marked for JSON deserialization
    [JsonConstructor]
    public IgnoreRule() => logger = NullLogger.Instance; // Assign a default logger

    /// <summary>
    /// Initializes a new instance of the <see cref="IgnoreRule"/> class.
    /// Constructor for programmatic creation with logger.
    /// </summary>
    public IgnoreRule(ILogger? logger = null) => this.logger = logger ?? NullLogger.Instance;

    /// <summary>
    /// Gets or sets path to the property (e.g., "Body.Response.Results[0].Name").
    /// </summary>
    public string PropertyPath
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets a value indicating whether whether to completely ignore this property during comparison.
    /// </summary>
    public bool IgnoreCompletely { get; set; } = false;

    /// <summary>
    /// Gets or sets a value indicating whether for collections, whether to ignore the order of items.
    /// </summary>
    public bool IgnoreCollectionOrder { get; set; } = false;

    /// <summary>
    /// Applies this rule to the comparison configuration
    /// PERFORMANCE CRITICAL: Smart minimal pattern generation.
    /// </summary>
    public void ApplyTo(ComparisonConfig config)
    {
        if (string.IsNullOrEmpty(PropertyPath))
        {
            return;
        }

        if (IgnoreCompletely)
        {
            logger.LogDebug("Applying ignore rule for: {PropertyPath}", PropertyPath);

            // SMART MINIMAL APPROACH: Add the exact pattern + essential collection patterns

            // 1. Add the original path exactly as specified
            if (!config.MembersToIgnore.Contains(PropertyPath, StringComparer.Ordinal))
            {
                config.MembersToIgnore.Add(PropertyPath);
                logger.LogDebug("Added original path to ignore: {PropertyPath}", PropertyPath);
            }

            // 2. Add ESSENTIAL collection variations for CompareNetObjects compatibility
            // Only add patterns that are likely to exist based on the property name
            AddSmartCollectionPatterns(config, PropertyPath);

            logger.LogDebug("Final ignore list contains {Count} entries", config.MembersToIgnore.Count);

            // Removed verbose logging for performance - patterns are logged at debug level only
        }
        else
        {
            // If not ignoring completely, ensure it's NOT in MembersToIgnore
            if (config.MembersToIgnore.Contains(PropertyPath, StringComparer.Ordinal))
            {
                config.MembersToIgnore.Remove(PropertyPath);
            }

            // Property-specific collection order is handled by PropertySpecificCollectionOrderComparer
            if (IgnoreCollectionOrder)
            {
                logger.LogDebug("Property {PropertyPath} has collection order ignoring enabled", PropertyPath);

                // No direct config change here; the custom comparer handles it.
            }
        }
    }

    /// <summary>
    /// Add COMPREHENSIVE collection patterns - catch ALL the ways CompareNetObjects accesses collections
    /// PERFORMANCE OPTIMIZED: Still minimal but comprehensive coverage.
    /// </summary>
    private void AddSmartCollectionPatterns(ComparisonConfig config, string propertyPath)
    {
        var patterns = new List<string>();

        // Extract the base collection path (everything before the first [)
        var baseCollectionPath = propertyPath.Split('[')[0];

        // COMPREHENSIVE PATTERNS: Cover all the ways CompareNetObjects might access collections

        // 1. REMOVED: Basic System.Collections patterns were too broad
        // These patterns were accidentally matching ALL properties in collections
        // Instead, we only add specific property patterns below

        // 2. CLEANED UP: Only add specific property patterns, not broad collection patterns
        // The old code was adding too many variations that could match unintended properties

        // 3. Always include the original pattern
        patterns.Add(propertyPath);

        // 4. PRECISION FIX: Add EXACT patterns that CompareNetObjects uses
        // CompareNetObjects generates paths like: "ResponseMetadata.Performance.ComponentTimings[0].CallCount"
        if (propertyPath.Contains("[*]."))
        {
            // Handle specific property within collection: "Collection[*].Property"
            var beforeBracket = propertyPath.Substring(0, propertyPath.IndexOf("[*]", StringComparison.Ordinal));
            var afterBracket = propertyPath.Substring(propertyPath.IndexOf("[*].", StringComparison.Ordinal) + 4);

            // CRITICAL: Add ONLY exact numbered index patterns for the specific property
            // This prevents accidentally ignoring other properties in the same collection
            for (var i = 0; i < 10; i++)
            {
                patterns.Add($"{beforeBracket}[{i}].{afterBracket}");
            }

            // Also add System.Collections patterns but ONLY for the exact property
            patterns.Add($"{beforeBracket}.System.Collections.IList.Item[*].{afterBracket}");
            patterns.Add($"{beforeBracket}.System.Collections.Generic.IList`1.Item[*].{afterBracket}");

            // PRECISION VALIDATION: Log what we're ignoring to prevent over-matching
            logger.LogInformation(
                "Generated PRECISE patterns for {PropertyPath}: Will ignore ONLY '{SpecificProperty}', NOT other collection properties",
                propertyPath,
                afterBracket);
        }
        else if (IsLikelyCollectionPath(propertyPath))
        {
            // COLLECTION-LEVEL IGNORE: Handle entire collection ignores like "Metadata.Performance.ComponentTimings"
            // This should ignore ALL properties within the collection
            logger.LogInformation(
                "Detected collection-level ignore for {PropertyPath}: Will ignore ALL properties within this collection",
                propertyPath);

            // Add patterns to match ALL indexed access to this collection
            for (var i = 0; i < 10; i++)
            {
                patterns.Add($"{propertyPath}[{i}]");
            }

            // Add System.Collections patterns for the entire collection
            patterns.Add($"{propertyPath}.System.Collections.IList.Item[*]");
            patterns.Add($"{propertyPath}.System.Collections.Generic.IList`1.Item[*]");

            // Add patterns for numbered System.Collections access
            for (var i = 0; i < 10; i++)
            {
                patterns.Add($"{propertyPath}.System.Collections.IList.Item[{i}]");
                patterns.Add($"{propertyPath}.System.Collections.Generic.IList`1.Item[{i}]");
            }
        }

        // 6. PERFORMANCE: Removed wildcard catch-all pattern to reduce overhead

        // Add only the patterns that don't already exist with enhanced validation
        var addedPatterns = new List<string>();
        foreach (var pattern in patterns.Distinct(StringComparer.Ordinal))
        {
            if (!config.MembersToIgnore.Contains(pattern, StringComparer.Ordinal))
            {
                config.MembersToIgnore.Add(pattern);
                addedPatterns.Add(pattern);
                logger.LogDebug("Added collection pattern: {Pattern} for rule: {OriginalRule}", pattern, PropertyPath);
            }
        }

        // SAFETY CHECK: Warn if we're generating too many patterns for a single rule
        if (addedPatterns.Count > 10)
        {
            logger.LogWarning(
                "Rule for {PropertyPath} generated {PatternCount} ignore patterns. This may be overly broad.",
                PropertyPath,
                addedPatterns.Count);
        }
    }

    /// <summary>
    /// Determine if a property path is likely to be a collection that should support entire-collection ignoring.
    /// </summary>
    private bool IsLikelyCollectionPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath) || propertyPath.Contains("["))
        {
            return false;
        }

        // Common collection naming patterns
        var collectionIndicators = new[]
        {
            "Timings", "Results", "Items", "List", "Array", "Collection",
            "Data", "Elements", "Entries", "Records", "Values", "Components",
        };

        // Get the last segment of the path (the actual property name)
        var segments = propertyPath.Split('.');
        var lastSegment = segments[segments.Length - 1];

        // Check if it matches common collection naming patterns
        var isCollection = collectionIndicators.Any(indicator =>
            lastSegment.EndsWith(indicator, StringComparison.OrdinalIgnoreCase));

        if (isCollection)
        {
            logger.LogDebug(
                "Path '{PropertyPath}' detected as collection based on naming pattern '{LastSegment}'",
                propertyPath,
                lastSegment);
        }

        return isCollection;
    }

    /// <summary>
    /// Normalize a property path to handle variations in XML deserialization.
    /// </summary>
    private string NormalizePropertyPath(string propertyPath) => PropertyPathNormalizer.NormalizePropertyPath(propertyPath, logger);

    /// <summary>
    /// Add MINIMAL collection variations - only the most essential patterns
    /// PERFORMANCE CRITICAL: This replaces the explosive pattern generation.
    /// </summary>
    private void AddMinimalCollectionVariations(ComparisonConfig config, string propertyPath)
    {
        // Only add wildcard pattern if path doesn't already contain array notation
        if (!propertyPath.Contains("[") && propertyPath.Contains("."))
        {
            // For complex paths like "Body.Response.Results.Description"
            // Add just the wildcard version: "Body.Response.Results[*].Description"
            var segments = propertyPath.Split('.');
            for (var i = 0; i < segments.Length - 1; i++)
            {
                var segment = segments[i];

                // Only for likely collection names
                if (segment.EndsWith("s", StringComparison.Ordinal) || segment.Contains("Results") || segment.Contains("Items"))
                {
                    var prefix = string.Join(".", segments.Take(i + 1));
                    var suffix = string.Join(".", segments.Skip(i + 1));
                    var wildcardPath = $"{prefix}[*].{suffix}";

                    if (!config.MembersToIgnore.Contains(wildcardPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(wildcardPath);
                        logger.LogDebug("Added minimal wildcard: {Path}", wildcardPath);
                    }

                    break; // Only do this for the first likely collection
                }
            }
        }

        // For simple property names, add common collection patterns
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
        {
            var essentialCollections = new[] { "Results", "Items" };
            foreach (var collection in essentialCollections)
            {
                var pattern = $"{collection}[*].{propertyPath}";
                if (!config.MembersToIgnore.Contains(pattern, StringComparer.Ordinal))
                {
                    config.MembersToIgnore.Add(pattern);
                    logger.LogDebug("Added minimal collection pattern: {Pattern}", pattern);
                }
            }
        }
    }

    /// <summary>
    /// Add collection variations for properties that may be inside collections
    /// Performance optimized - reduced from 20 variations to 5 strategic ones.
    /// </summary>
    private void AddCollectionVariations(ComparisonConfig config, string propertyPath)
    {
        logger.LogDebug("Adding optimized collection variations for: {PropertyPath}", propertyPath);

        var segments = propertyPath.Split('.');

        // Performance optimization: Only generate patterns for segments likely to be collections
        var collectionIndicators = new[] { "Results", "Items", "List", "Array", "Collection", "Data", "Elements" };

        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            // Only process segments that are likely collections
            var isLikelyCollection = collectionIndicators.Any(indicator =>
                segment.Contains(indicator, StringComparison.OrdinalIgnoreCase));

            if (isLikelyCollection)
            {
                // Create the path prefix up to this segment
                var prefix = string.Join(".", segments.Take(i + 1));

                // Create the path suffix from remaining segments
                var suffix = i < segments.Length - 1
                    ? "." + string.Join(".", segments.Skip(i + 1))
                    : string.Empty;

                // Add only essential indexed versions (reduced from 20 to 5)
                var essentialIndices = new[] { 0, 1, 2, 3, 4 }; // Cover most common use cases
                foreach (var idx in essentialIndices)
                {
                    var indexedPath = $"{prefix}[{idx}]{suffix}";
                    if (!config.MembersToIgnore.Contains(indexedPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(indexedPath);
                        logger.LogDebug("Adding indexed variation: {Path}", indexedPath);
                    }
                }

                // Add wildcard version (most important)
                var wildcardPath = $"{prefix}[*]{suffix}";
                if (!config.MembersToIgnore.Contains(wildcardPath, StringComparer.Ordinal))
                {
                    config.MembersToIgnore.Add(wildcardPath);
                    logger.LogDebug("Adding wildcard variation: {Path}", wildcardPath);
                }
            }
        }

        // Special handling for simple property names - but much more targeted
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
        {
            var propertyName = propertyPath;

            // Use only the most common collections and reduce variations
            var commonCollections = new[] { "Results", "Items" }; // Reduced from 7 to 2 most common

            foreach (var collection in commonCollections)
            {
                // Add wildcard version (most important)
                var wildcardPath = $"{collection}[*].{propertyName}";
                if (!config.MembersToIgnore.Contains(wildcardPath, StringComparer.Ordinal))
                {
                    config.MembersToIgnore.Add(wildcardPath);
                    logger.LogDebug("Added wildcard collection path: {Path}", wildcardPath);
                }

                // Add only first 3 numbered versions (reduced from 20 to 3)
                for (var idx = 0; idx < 3; idx++)
                {
                    var numberedPath = $"{collection}[{idx}].{propertyName}";
                    if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(numberedPath);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add variations of the property path to handle collection items.
    /// </summary>
    private void AddCollectionVariationsOld(ComparisonConfig config, string propertyPath)
    {
        try
        {
            logger.LogDebug("Creating collection variations for: {PropertyPath}", propertyPath);

            // Handle paths that already contain [*] - convert to numbered indices
            if (propertyPath.Contains("[*]"))
            {
                logger.LogDebug("Path contains [*], creating numbered variations");

                // Create numbered index variations (0-19 to cover more cases)
                for (var idx = 0; idx < 20; idx++)
                {
                    var numberedPath = propertyPath.Replace("[*]", $"[{idx}]");
                    if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(numberedPath);
                        logger.LogDebug("Added numbered variation: {Path}", numberedPath);
                    }
                }

                // Add variations without dynamic prefixes (domain-agnostic)
                AddDynamicPrefixVariationsForPattern(config, propertyPath);

                // Add pattern-based variations
                AddPatternBasedVariations(config, propertyPath);

                // Add System.Collections.IList.Item[*] variations for comparison library collection handling
                AddSystemCollectionVariations(config, propertyPath);
            }
            else
            {
                // Handle paths without [*] - use the original complex logic
                AddComplexCollectionVariations(config, propertyPath);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding collection variations for {PropertyPath}", propertyPath);
        }
    }

    /// <summary>
    /// Add pattern-based variations for property paths.
    /// </summary>
    private void AddPatternBasedVariations(ComparisonConfig config, string propertyPath)
    {
        // Add regex-style patterns that the comparison engine might recognize
        var patterns = new[]
        {
            propertyPath.Replace("[*]", @"\[\d+\]"),  // Regex pattern
            propertyPath.Replace("[*]", "*"),         // Simple wildcard
            propertyPath.Replace("[*]", ".*"),        // Regex wildcard
        };

        foreach (var pattern in patterns)
        {
            if (!config.MembersToIgnore.Contains(pattern, StringComparer.Ordinal))
            {
                config.MembersToIgnore.Add(pattern);
                logger.LogDebug("Added pattern variation: {Pattern}", pattern);
            }
        }
    }

    /// <summary>
    /// Add complex collection variations (original logic).
    /// </summary>
    private void AddComplexCollectionVariations(ComparisonConfig config, string propertyPath)
    {
        // Split the path into segments
        var segments = propertyPath.Split('.');

        // For each segment, check if it might be a collection
        for (var i = 0; i < segments.Length; i++)
        {
            var segment = segments[i];

            // If this is a likely collection name
            var isCollection = segment.EndsWith("s", StringComparison.Ordinal) || segment.EndsWith("Results", StringComparison.Ordinal) ||
                              segment.EndsWith("Items", StringComparison.Ordinal) || segment.EndsWith("List", StringComparison.Ordinal) ||
                              segment.EndsWith("Collection", StringComparison.Ordinal) || segment.EndsWith("Array", StringComparison.Ordinal);

            if (isCollection)
            {
                // Create the path prefix up to this segment
                var prefix = string.Join(".", segments.Take(i + 1));

                // Create the path suffix from remaining segments
                var suffix = i < segments.Length - 1
                    ? "." + string.Join(".", segments.Skip(i + 1))
                    : string.Empty;

                // Add indexed versions
                for (var idx = 0; idx < 20; idx++)
                {
                    var indexedPath = $"{prefix}[{idx}]{suffix}";
                    if (!config.MembersToIgnore.Contains(indexedPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(indexedPath);
                        logger.LogDebug("Adding indexed variation: {Path}", indexedPath);
                    }
                }

                // Add wildcard version
                var wildcardPath = $"{prefix}[*]{suffix}";
                if (!config.MembersToIgnore.Contains(wildcardPath, StringComparer.Ordinal))
                {
                    config.MembersToIgnore.Add(wildcardPath);
                    logger.LogDebug("Adding wildcard variation: {Path}", wildcardPath);
                }
            }
        }

        // Special handling for simple property names
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
        {
            var propertyName = propertyPath;

            // Add to common collections with both numbered and wildcard versions
            var collections = new[] { "Results", "RelatedItems", "Items", "Data", "Elements", "List", "Array" };

            // Use only empty prefix for domain-agnostic approach
            var prefixes = new[] { string.Empty };

            foreach (var prefix in prefixes)
            {
                foreach (var collection in collections)
                {
                    // Wildcard version
                    var wildcardPath = $"{prefix}{collection}[*].{propertyName}";
                    if (!config.MembersToIgnore.Contains(wildcardPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(wildcardPath);
                        logger.LogDebug("Added wildcard collection path: {Path}", wildcardPath);
                    }

                    // Numbered versions
                    for (var idx = 0; idx < 20; idx++)
                    {
                        var numberedPath = $"{prefix}{collection}[{idx}].{propertyName}";
                        if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
                        {
                            config.MembersToIgnore.Add(numberedPath);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add System.Collections.IList.Item[*] variations for comparison library collection handling.
    /// </summary>
    private void AddSystemCollectionVariations(ComparisonConfig config, string propertyPath)
    {
        if (!propertyPath.Contains("[*]"))
        {
            return;
        }

        logger.LogDebug("Creating System.Collections variations for: {PropertyPath}", propertyPath);

        // Convert [*] patterns to System.Collections.IList.Item[*] patterns
        // Example: "Body.Response.Results[*].Details" becomes "Body.Response.Results.System.Collections.IList.Item[*].Details"
        var systemCollectionPath = propertyPath.Replace("[*]", ".System.Collections.IList.Item[*]");

        if (!config.MembersToIgnore.Contains(systemCollectionPath, StringComparer.Ordinal))
        {
            config.MembersToIgnore.Add(systemCollectionPath);
            logger.LogDebug("Added System.Collections variation: {Path}", systemCollectionPath);
        }

        // Also add numbered variations
        for (var idx = 0; idx < 20; idx++)
        {
            var numberedPath = systemCollectionPath.Replace("[*]", $"[{idx}]");
            if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
            {
                config.MembersToIgnore.Add(numberedPath);
                logger.LogDebug("Added numbered System.Collections variation: {Path}", numberedPath);
            }
        }

        // Add variations with dynamic prefixes removed (domain-agnostic)
        AddDynamicPrefixVariationsForSystemCollections(config, systemCollectionPath);
    }

    /// <summary>
    /// Add path variations by dynamically removing path segments from the beginning.
    /// This is domain-agnostic and works with any path structure.
    /// </summary>
    private void AddDynamicPrefixVariations(ComparisonConfig config, string propertyPath)
    {
        var segments = propertyPath.Split('.');

        // Create variations by removing 1, 2, 3... segments from the beginning
        for (var segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
        {
            if (segments.Length > segmentsToRemove)
            {
                var shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                if (!string.IsNullOrEmpty(shortenedPath) && !config.MembersToIgnore.Contains(shortenedPath, StringComparer.Ordinal))
                {
                    config.MembersToIgnore.Add(shortenedPath);
                    logger.LogDebug("Added dynamic prefix variation (removed {Count} segments): {Path}", segmentsToRemove, shortenedPath);

                    // Also generate collection variations for this shortened path
                    AddCollectionVariations(config, shortenedPath);
                }
            }
        }
    }

    /// <summary>
    /// Add dynamic prefix variations for patterns containing [*].
    /// </summary>
    private void AddDynamicPrefixVariationsForPattern(ComparisonConfig config, string propertyPath)
    {
        var segments = propertyPath.Split('.');

        // Create variations by removing 1, 2, 3... segments from the beginning
        for (var segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
        {
            if (segments.Length > segmentsToRemove)
            {
                var shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                if (!string.IsNullOrEmpty(shortenedPath))
                {
                    // Add numbered variations for this shortened path
                    for (var idx = 0; idx < 20; idx++)
                    {
                        var numberedPath = shortenedPath.Replace("[*]", $"[{idx}]");
                        if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
                        {
                            config.MembersToIgnore.Add(numberedPath);
                            logger.LogDebug("Added dynamic prefix variation (removed {Count} segments, numbered): {Path}", segmentsToRemove, numberedPath);
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Add dynamic prefix variations for System.Collections paths.
    /// </summary>
    private void AddDynamicPrefixVariationsForSystemCollections(ComparisonConfig config, string systemCollectionPath)
    {
        var segments = systemCollectionPath.Split('.');

        // Create variations by removing 1, 2, 3... segments from the beginning
        for (var segmentsToRemove = 1; segmentsToRemove < segments.Length; segmentsToRemove++)
        {
            if (segments.Length > segmentsToRemove)
            {
                var shortenedPath = string.Join(".", segments.Skip(segmentsToRemove));
                if (!string.IsNullOrEmpty(shortenedPath))
                {
                    if (!config.MembersToIgnore.Contains(shortenedPath, StringComparer.Ordinal))
                    {
                        config.MembersToIgnore.Add(shortenedPath);
                        logger.LogDebug("Added dynamic System.Collections variation (removed {Count} segments): {Path}", segmentsToRemove, shortenedPath);
                    }

                    // Add numbered variations
                    for (var idx = 0; idx < 20; idx++)
                    {
                        var numberedPath = shortenedPath.Replace("[*]", $"[{idx}]");
                        if (!config.MembersToIgnore.Contains(numberedPath, StringComparer.Ordinal))
                        {
                            config.MembersToIgnore.Add(numberedPath);
                            logger.LogDebug("Added dynamic System.Collections variation (removed {Count} segments, numbered): {Path}", segmentsToRemove, numberedPath);
                        }
                    }
                }
            }
        }
    }
}
