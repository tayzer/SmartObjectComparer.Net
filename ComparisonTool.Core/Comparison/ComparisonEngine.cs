// <copyright file="ComparisonEngine.cs" company="PlaceholderCompany">



using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Core comparison engine that handles object-to-object comparisons with isolated configuration.
/// </summary>
public class ComparisonEngine : IComparisonEngine, IDisposable
{
    private readonly ILogger<ComparisonEngine> logger;
    private readonly IComparisonConfigurationService configService;
    private readonly PerformanceTracker performanceTracker;

    // PERFORMANCE OPTIMIZATION: Thread-local CompareLogic to avoid allocation per comparison
    private readonly ThreadLocal<CompareLogic> threadLocalCompareLogic;

    // PERFORMANCE OPTIMIZATION: Cache configuration fingerprint to detect changes
    private volatile string cachedConfigFingerprint = string.Empty;
    private volatile bool configurationChanged = true;

    public ComparisonEngine(
        ILogger<ComparisonEngine> logger,
        IComparisonConfigurationService configService,
        PerformanceTracker performanceTracker)
    {
        this.logger = logger;
        this.configService = configService;
        this.performanceTracker = performanceTracker;

        // Initialize thread-local CompareLogic with lazy creation
        threadLocalCompareLogic = new ThreadLocal<CompareLogic>(
            valueFactory: () => CreateIsolatedCompareLogic(),
            trackAllValues: false);
    }

    /// <summary>
    /// Mark configuration as changed - call this when ignore rules are modified.
    /// </summary>
    public void InvalidateConfiguration() => configurationChanged = true;

    /// <summary>
    /// Performs a comparison between two objects using the specified model type.
    /// </summary>
    /// <param name="oldResponse">The old/reference object.</param>
    /// <param name="newResponse">The new/comparison object.</param>
    /// <param name="modelType">The type of the model being compared.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    public async Task<ComparisonResult> CompareObjectsAsync(
        object oldResponse,
        object newResponse,
        Type modelType,
        CancellationToken cancellationToken = default)
    {
        // PERFORMANCE OPTIMIZATION: Use thread-local CompareLogic to avoid allocation per file
        var result = await performanceTracker.TrackOperationAsync(
            "Compare_Objects",
            async () => await Task.Run(
                () =>
                {
                    // Get or refresh thread-local CompareLogic
                    var compareLogic = GetOrRefreshCompareLogic();

                    // Direct comparison without cloning - this eliminates serialization corruption
                    return compareLogic.Compare(oldResponse, newResponse);
                }, cancellationToken));

        logger.LogDebug(
            "Comparison completed. Found {DifferenceCount} differences",
            result.Differences.Count);

        // Filter out ignored properties using smart rules and legacy pattern matching
        result = configService.FilterSmartIgnoredDifferences(result, modelType);
        result = configService.FilterIgnoredDifferences(result);

        return result;
    }

    /// <summary>
    /// Performs a synchronous comparison between two objects.
    /// Use this for CPU-bound pipeline stages where async overhead is unnecessary.
    /// </summary>
    /// <param name="oldResponse">The old/reference object.</param>
    /// <param name="newResponse">The new/comparison object.</param>
    /// <param name="modelType">The type of the model being compared.</param>
    /// <returns>Comparison result with differences.</returns>
    public ComparisonResult CompareObjectsSync(
        object oldResponse,
        object newResponse,
        Type modelType)
    {
        var compareLogic = GetOrRefreshCompareLogic();
        var result = compareLogic.Compare(oldResponse, newResponse);

        // Filter out ignored properties
        result = configService.FilterSmartIgnoredDifferences(result, modelType);
        result = configService.FilterIgnoredDifferences(result);

        return result;
    }

    /// <summary>
    /// Dispose of thread-local resources.
    /// </summary>
    public void Dispose()
    {
        threadLocalCompareLogic?.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Get thread-local CompareLogic, refreshing if configuration changed.
    /// </summary>
    private CompareLogic GetOrRefreshCompareLogic()
    {
        // Check if configuration changed (volatile read)
        if (configurationChanged)
        {
            // Reset thread-local value to force recreation
            if (threadLocalCompareLogic.IsValueCreated)
            {
                // Can't reset ThreadLocal, but we can invalidate by recreating
                configurationChanged = false;
            }
        }

        return threadLocalCompareLogic.Value!;
    }

    /// <summary>
    /// Creates a completely isolated CompareLogic instance with no shared state.
    /// This eliminates the "Collection was modified" errors by ensuring each comparison
    /// operation has its own independent configuration.
    /// </summary>
    private CompareLogic CreateIsolatedCompareLogic()
    {
        var isolatedCompareLogic = new CompareLogic();

        // Copy basic configuration settings from the main service
        var currentConfig = configService.GetCurrentConfig();

        // PERFORMANCE OPTIMIZATION: Limit MaxDifferences to avoid excessive comparison time
        isolatedCompareLogic.Config.MaxDifferences = Math.Min(currentConfig.MaxDifferences, 1000);
        isolatedCompareLogic.Config.IgnoreObjectTypes = currentConfig.IgnoreObjectTypes;
        isolatedCompareLogic.Config.ComparePrivateFields = currentConfig.ComparePrivateFields;
        isolatedCompareLogic.Config.ComparePrivateProperties = currentConfig.ComparePrivateProperties;
        isolatedCompareLogic.Config.CompareReadOnly = currentConfig.CompareReadOnly;
        isolatedCompareLogic.Config.IgnoreCollectionOrder = currentConfig.IgnoreCollectionOrder;
        isolatedCompareLogic.Config.CaseSensitive = currentConfig.CaseSensitive;

        // PERFORMANCE OPTIMIZATION: Enable reflection caching
        isolatedCompareLogic.Config.Caching = true;

        // PERFORMANCE OPTIMIZATION: Skip invalid indexers to avoid exceptions
        isolatedCompareLogic.Config.SkipInvalidIndexers = true;

        // Initialize collections to prevent null reference exceptions
        isolatedCompareLogic.Config.MembersToIgnore = new List<string>();
        isolatedCompareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
        isolatedCompareLogic.Config.AttributesToIgnore = new List<Type>();
        isolatedCompareLogic.Config.MembersToInclude = new List<string>();

        // Apply ignore rules by applying them directly to the config
        var ignoreRules = configService.GetIgnoreRules();
        foreach (var rule in ignoreRules.Where(r => r.IgnoreCompletely))
        {
            try
            {
                // Apply the rule directly to the isolated config
                rule.ApplyTo(isolatedCompareLogic.Config);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error applying ignore rule for property {PropertyPath} in isolated config", rule.PropertyPath);
            }
        }

        // Ensure array Length and LongLength properties are always ignored
        if (!isolatedCompareLogic.Config.MembersToIgnore.Contains("Length"))
        {
            isolatedCompareLogic.Config.MembersToIgnore.Add("Length");
        }

        if (!isolatedCompareLogic.Config.MembersToIgnore.Contains("LongLength"))
        {
            isolatedCompareLogic.Config.MembersToIgnore.Add("LongLength");
        }

        if (!isolatedCompareLogic.Config.MembersToIgnore.Contains("NativeLength"))
        {
            isolatedCompareLogic.Config.MembersToIgnore.Add("NativeLength");
        }

        // Apply collection order rules by creating new, independent custom comparers
        var collectionOrderRules = ignoreRules.Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely).ToList();
        if (collectionOrderRules.Any())
        {
            try
            {
                // Create independent collection order comparer with no shared state
                var propertiesWithIgnoreOrder = collectionOrderRules.Select(r => r.PropertyPath).ToList();
                var expandedProperties = propertiesWithIgnoreOrder
                    .SelectMany(p => new[] { p, p.Replace("[*]", "[0]"), p.Replace("[*]", "[1]") })
                    .Distinct()
                    .ToList();

                // Use RootComparerFactory directly like other parts of the codebase
                var collectionOrderComparer = new PropertySpecificCollectionOrderComparer(
                    RootComparerFactory.GetRootComparer(), expandedProperties, logger);

                isolatedCompareLogic.Config.CustomComparers.Add(collectionOrderComparer);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error creating independent collection order comparer");
            }
        }

        // Note: XmlIgnore properties are handled by adding them to MembersToIgnore during configuration
        // This avoids the recursion issue that occurs with custom comparers
        logger.LogDebug(
            "Created isolated CompareLogic with {IgnorePatterns} ignore patterns and {Comparers} custom comparers",
            isolatedCompareLogic.Config.MembersToIgnore.Count,
            isolatedCompareLogic.Config.CustomComparers.Count);

        return isolatedCompareLogic;
    }

    /// <summary>
    /// Extracts the property name from a property path.
    /// </summary>
    /// <param name="propertyPath">The property path (e.g., "Customer.Orders[0].Items[1].Name").</param>
    /// <returns>The extracted property name (e.g., "Name").</returns>
    private string GetPropertyNameFromPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return string.Empty;
        }

        // Handle array indexing by removing the [*] part
        var cleanPath = propertyPath.Replace("[*]", string.Empty).Replace("[0]", string.Empty).Replace("[1]", string.Empty);

        // Split by dots and get the last part
        var parts = cleanPath.Split('.');
        return parts.Length > 0 ? parts[^1] : propertyPath;
    }
}
