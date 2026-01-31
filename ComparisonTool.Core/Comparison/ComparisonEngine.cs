using System;
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
    private readonly ThreadLocal<CompareLogic> threadLocalCompareLogic;
    private volatile string? cachedConfigFingerprint;
    private volatile bool configurationChanged = true;

    public ComparisonEngine(
        ILogger<ComparisonEngine> comparisonLogger,
        IComparisonConfigurationService configurationService,
        PerformanceTracker performanceTrackerInstance)
    {
        logger = comparisonLogger;
        configService = configurationService;
        performanceTracker = performanceTrackerInstance;
        threadLocalCompareLogic = new ThreadLocal<CompareLogic>(
            valueFactory: () => CreateIsolatedCompareLogic(),
            trackAllValues: false);
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
    /// Mark configuration as changed - call this when ignore rules are modified.
    /// </summary>
    public void InvalidateConfiguration()
    {
        configurationChanged = true;
    }

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
        var result = await performanceTracker.TrackOperationAsync(
                "Compare_Objects",
                async () =>
                {
                    return await Task.Run(
                            () =>
                            {
                                var compareLogic = GetOrRefreshCompareLogic();
                                return compareLogic.Compare(oldResponse, newResponse);
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                })
            .ConfigureAwait(false);

        logger.LogDebug(
            "Comparison completed. Found {DifferenceCount} differences",
            result.Differences.Count);

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

        result = configService.FilterSmartIgnoredDifferences(result, modelType);
        result = configService.FilterIgnoredDifferences(result);

        return result;
    }

    private CompareLogic GetOrRefreshCompareLogic()
    {
        if (configurationChanged)
        {
            if (threadLocalCompareLogic.IsValueCreated)
            {
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
        var currentConfig = configService.GetCurrentConfig();

        isolatedCompareLogic.Config.MaxDifferences = Math.Min(currentConfig.MaxDifferences, 1000);
        isolatedCompareLogic.Config.IgnoreObjectTypes = currentConfig.IgnoreObjectTypes;
        isolatedCompareLogic.Config.ComparePrivateFields = currentConfig.ComparePrivateFields;
        isolatedCompareLogic.Config.ComparePrivateProperties = currentConfig.ComparePrivateProperties;
        isolatedCompareLogic.Config.CompareReadOnly = currentConfig.CompareReadOnly;
        isolatedCompareLogic.Config.IgnoreCollectionOrder = currentConfig.IgnoreCollectionOrder;
        isolatedCompareLogic.Config.CaseSensitive = currentConfig.CaseSensitive;
        isolatedCompareLogic.Config.Caching = true;
        isolatedCompareLogic.Config.SkipInvalidIndexers = true;

        InitializeConfigCollections(isolatedCompareLogic);

        var ignoreRules = configService.GetIgnoreRules();
        ApplyIgnoreRules(isolatedCompareLogic, ignoreRules);
        EnsureDefaultMembersIgnored(isolatedCompareLogic);
        ApplyCollectionOrderRules(isolatedCompareLogic, ignoreRules);

        logger.LogDebug(
            "Created isolated CompareLogic with {IgnorePatterns} ignore patterns and {Comparers} custom comparers",
            isolatedCompareLogic.Config.MembersToIgnore.Count,
            isolatedCompareLogic.Config.CustomComparers.Count);

        return isolatedCompareLogic;
    }

    private void InitializeConfigCollections(CompareLogic compareLogic)
    {
        compareLogic.Config.MembersToIgnore = new List<string>();
        compareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
        compareLogic.Config.AttributesToIgnore = new List<Type>();
        compareLogic.Config.MembersToInclude = new List<string>();
    }

    private void EnsureDefaultMembersIgnored(CompareLogic compareLogic)
    {
        if (!compareLogic.Config.MembersToIgnore.Contains("Length", System.StringComparer.Ordinal))
        {
            compareLogic.Config.MembersToIgnore.Add("Length");
        }

        if (!compareLogic.Config.MembersToIgnore.Contains("LongLength", System.StringComparer.Ordinal))
        {
            compareLogic.Config.MembersToIgnore.Add("LongLength");
        }

        if (!compareLogic.Config.MembersToIgnore.Contains("NativeLength", System.StringComparer.Ordinal))
        {
            compareLogic.Config.MembersToIgnore.Add("NativeLength");
        }
    }

    private void ApplyIgnoreRules(CompareLogic compareLogic, IEnumerable<IgnoreRule> ignoreRules)
    {
        foreach (var rule in ignoreRules.Where(r => r.IgnoreCompletely))
        {
            try
            {
                rule.ApplyTo(compareLogic.Config);
            }
            catch (Exception ex)
            {
                logger.LogWarning(
                    ex,
                    "Error applying ignore rule for property {PropertyPath} in isolated config",
                    rule.PropertyPath);
            }
        }
    }

    private void ApplyCollectionOrderRules(CompareLogic compareLogic, IEnumerable<IgnoreRule> ignoreRules)
    {
        var collectionOrderRules = ignoreRules.Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely).ToList();
        if (!collectionOrderRules.Any())
        {
            return;
        }

        try
        {
            var propertiesWithIgnoreOrder = collectionOrderRules.Select(r => r.PropertyPath).ToList();
            var expandedProperties = propertiesWithIgnoreOrder
                .SelectMany(p => new[] { p, p.Replace("[*]", "[0]"), p.Replace("[*]", "[1]") })
                .Distinct(System.StringComparer.Ordinal)
                .ToList();

            var collectionOrderComparer = new PropertySpecificCollectionOrderComparer(
                RootComparerFactory.GetRootComparer(),
                expandedProperties,
                logger);

            compareLogic.Config.CustomComparers.Add(collectionOrderComparer);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error creating independent collection order comparer");
        }
    }

    private string GetPropertyNameFromPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return string.Empty;
        }

        var cleanPath = propertyPath.Replace("[*]", string.Empty).Replace("[0]", string.Empty).Replace("[1]", string.Empty);
        var parts = cleanPath.Split('.');
        return parts.Length > 0 ? parts[^1] : propertyPath;
    }
}
