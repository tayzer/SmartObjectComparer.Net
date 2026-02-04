using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.Comparison.Configuration;

/// <summary>
/// Service responsible for managing configuration settings for object comparison.
/// </summary>
public class ComparisonConfigurationService : IComparisonConfigurationService
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<ComparisonConfigurationService> logger;
    private readonly CompareLogic compareLogic;
    private readonly List<IgnoreRule> ignoreRules = new List<IgnoreRule>();
    private readonly List<SmartIgnoreRule> smartIgnoreRules = new List<SmartIgnoreRule>();
    private readonly SmartIgnoreProcessor smartIgnoreProcessor;
    private readonly List<IgnoreRule> xmlIgnoreRules = new List<IgnoreRule>();
    private readonly object configurationLock = new object();

    private ComparisonResultCacheService? cacheService;

    // Performance optimization: Cache compiled configuration to avoid rebuilding for every file
    private bool isConfigurationDirty = true;
    private string lastConfigurationFingerprint = string.Empty;
    private CachedComparisonConfig? cachedConfig = null;

    public ComparisonConfigurationService(
        ILogger<ComparisonConfigurationService> logger,
        IOptions<ComparisonConfigurationOptions>? options = null)
    {
        this.logger = logger;
        smartIgnoreProcessor = new SmartIgnoreProcessor(logger);

        var configOptions = options?.Value ?? new ComparisonConfigurationOptions();

        compareLogic = new CompareLogic
        {
            Config = new ComparisonConfig
            {
                MaxDifferences = configOptions.MaxDifferences,
                IgnoreObjectTypes = false,
                ComparePrivateFields = false,
                ComparePrivateProperties = true,
                CompareReadOnly = true,
                IgnoreCollectionOrder = configOptions.DefaultIgnoreCollectionOrder,
                CaseSensitive = !configOptions.DefaultIgnoreStringCase,
            },
        };

        // Ensure critical collections are initialized to prevent null reference exceptions
        // in CompareNetObjects internal logic like ExcludedByWildcard
        if (compareLogic.Config.MembersToIgnore == null)
        {
            compareLogic.Config.MembersToIgnore = new List<string>();
        }

        if (compareLogic.Config.CustomComparers == null)
        {
            compareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
        }

        if (compareLogic.Config.AttributesToIgnore == null)
        {
            compareLogic.Config.AttributesToIgnore = new List<Type>();
        }

        if (compareLogic.Config.MembersToInclude == null)
        {
            compareLogic.Config.MembersToInclude = new List<string>();
        }

        // Add array Length and LongLength properties to ignore list to prevent false differences
        // when arrays have different lengths but same content
        // Todo: this might cause issues for domains with the same property names
        compareLogic.Config.MembersToIgnore.Add("Length");
        compareLogic.Config.MembersToIgnore.Add("LongLength");
        compareLogic.Config.MembersToIgnore.Add("NativeLength");

        // Note: XmlIgnore properties will be automatically added to MembersToIgnore during configuration
        // This avoids the recursion issue that occurs with custom comparers
        this.logger.LogInformation(
            "Initialized ComparisonConfigurationService with MaxDifferences={MaxDiffs}, " +
                                   "IgnoreCollectionOrder={IgnoreOrder}, IgnoreCase={IgnoreCase}",
            configOptions.MaxDifferences,
            configOptions.DefaultIgnoreCollectionOrder,
            configOptions.DefaultIgnoreStringCase);
    }

    private IEnumerable<IgnoreRule> AllIgnoreRules => ignoreRules.Concat(xmlIgnoreRules);

    /// <summary>
    /// Set the cache service for invalidating cached results when configuration changes.
    /// </summary>
    public void SetCacheService(ComparisonResultCacheService cacheService) => this.cacheService = cacheService;

    /// <summary>
    /// Get the current comparison configuration.
    /// </summary>
    /// <returns></returns>
    public ComparisonConfig GetCurrentConfig() => compareLogic.Config;

    /// <summary>
    /// Get the compare logic instance.
    /// </summary>
    /// <returns></returns>
    public CompareLogic GetCompareLogic()
    {
        // Ensure critical collections are always initialized to prevent null reference exceptions
        // This is defensive programming against CompareNetObjects internal assumptions
        if (compareLogic.Config.MembersToIgnore == null)
        {
            compareLogic.Config.MembersToIgnore = new List<string>();
        }

        if (compareLogic.Config.CustomComparers == null)
        {
            compareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
        }

        if (compareLogic.Config.AttributesToIgnore == null)
        {
            compareLogic.Config.AttributesToIgnore = new List<Type>();
        }

        if (compareLogic.Config.MembersToInclude == null)
        {
            compareLogic.Config.MembersToInclude = new List<string>();
        }

        return compareLogic;
    }

    /// <summary>
    /// Get a thread-safe isolated CompareLogic instance for concurrent operations
    /// This prevents collection modification exceptions when multiple comparisons run in parallel.
    /// </summary>
    /// <returns></returns>
    public CompareLogic GetThreadSafeCompareLogic()
    {
        lock (configurationLock)
        {
            // Ensure configuration is up to date
            ApplyConfiguredSettings();

            // Create a new CompareLogic instance with the current configuration
            var isolatedCompareLogic = new CompareLogic();

            // Copy all configuration settings from the master instance
            isolatedCompareLogic.Config.MaxDifferences = compareLogic.Config.MaxDifferences;
            isolatedCompareLogic.Config.IgnoreObjectTypes = compareLogic.Config.IgnoreObjectTypes;
            isolatedCompareLogic.Config.ComparePrivateFields = compareLogic.Config.ComparePrivateFields;
            isolatedCompareLogic.Config.ComparePrivateProperties = compareLogic.Config.ComparePrivateProperties;
            isolatedCompareLogic.Config.CompareReadOnly = compareLogic.Config.CompareReadOnly;
            isolatedCompareLogic.Config.IgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder;
            isolatedCompareLogic.Config.CaseSensitive = compareLogic.Config.CaseSensitive;

            // Initialize critical collections to prevent null reference exceptions
            isolatedCompareLogic.Config.MembersToIgnore = new List<string>();
            isolatedCompareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
            isolatedCompareLogic.Config.AttributesToIgnore = new List<Type>();
            isolatedCompareLogic.Config.MembersToInclude = new List<string>();

            // Defensive copy of MembersToIgnore to prevent concurrent modification
            foreach (var member in compareLogic.Config.MembersToIgnore ?? new List<string>())
            {
                isolatedCompareLogic.Config.MembersToIgnore.Add(member);
            }

            // Defensive copy of CustomComparers
            foreach (var comparer in compareLogic.Config.CustomComparers ?? new List<BaseTypeComparer>())
            {
                isolatedCompareLogic.Config.CustomComparers.Add(comparer);
            }

            // Defensive copy of AttributesToIgnore
            foreach (var attribute in compareLogic.Config.AttributesToIgnore ?? new List<Type>())
            {
                isolatedCompareLogic.Config.AttributesToIgnore.Add(attribute);
            }

            // Defensive copy of MembersToInclude
            foreach (var member in compareLogic.Config.MembersToInclude ?? new List<string>())
            {
                isolatedCompareLogic.Config.MembersToInclude.Add(member);
            }

            logger.LogDebug(
                "Created thread-safe CompareLogic instance with {IgnoreCount} ignore patterns, {ComparerCount} custom comparers",
                isolatedCompareLogic.Config.MembersToIgnore.Count,
                isolatedCompareLogic.Config.CustomComparers.Count);

            return isolatedCompareLogic;
        }
    }

    /// <summary>
    /// Configure whether to ignore collection order.
    /// </summary>
    public void SetIgnoreCollectionOrder(bool ignoreOrder)
    {
        compareLogic.Config.IgnoreCollectionOrder = ignoreOrder;
        MarkConfigurationDirty();
        logger.LogDebug("Set IgnoreCollectionOrder to {Value}", ignoreOrder);

        // Invalidate cache when configuration changes
        if (cacheService != null)
        {
            var newFingerprint = cacheService.GenerateConfigurationFingerprint(this);
            cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Get whether collection order is being ignored.
    /// </summary>
    /// <returns></returns>
    public bool GetIgnoreCollectionOrder() => compareLogic.Config.IgnoreCollectionOrder;

    /// <summary>
    /// Configure whether to ignore string case.
    /// </summary>
    public void SetIgnoreStringCase(bool ignoreCase)
    {
        compareLogic.Config.CaseSensitive = !ignoreCase;
        MarkConfigurationDirty();
        logger.LogDebug("Set CaseSensitive to {Value} (IgnoreCase={IgnoreCase})", !ignoreCase, ignoreCase);

        // Invalidate cache when configuration changes
        if (cacheService != null)
        {
            var newFingerprint = cacheService.GenerateConfigurationFingerprint(this);
            cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Get whether string case is being ignored.
    /// </summary>
    /// <returns></returns>
    public bool GetIgnoreStringCase() => !compareLogic.Config.CaseSensitive;

    /// <summary>
    /// Configure the comparer to ignore specific properties.
    /// </summary>
    public void IgnoreProperty(string propertyPath)
    {
        var rule = new IgnoreRule(logger)
        {
            PropertyPath = propertyPath,
            IgnoreCompletely = true,
        };

        ignoreRules.Add(rule);
        logger.LogDebug("Added property {PropertyPath} to ignore list", propertyPath);
    }

    /// <summary>
    /// Remove a property from the ignore list.
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        var rulesToRemove = ignoreRules
            .Where(r => string.Equals(r.PropertyPath, propertyPath, StringComparison.Ordinal))
            .ToList();

        foreach (var rule in rulesToRemove)
        {
            ignoreRules.Remove(rule);
        }

        logger.LogDebug("Removed property {PropertyPath} from ignore list", propertyPath);
    }

    /// <summary>
    /// Add an ignore rule to the configuration.
    /// </summary>
    public void AddIgnoreRule(IgnoreRule rule)
    {
        if (rule == null)
        {
            return;
        }

        // Log the rule being added
        logger.LogDebug(
            "Adding rule for {PropertyPath} with settings: IgnoreCompletely={IgnoreCompletely}, IgnoreCollectionOrder={IgnoreOrder}",
            rule.PropertyPath,
            rule.IgnoreCompletely,
            rule.IgnoreCollectionOrder);

        RemoveIgnoredProperty(rule.PropertyPath);

        // Create a new rule with our logger to ensure proper logging
        var newRule = new IgnoreRule(logger)
        {
            PropertyPath = rule.PropertyPath,
            IgnoreCompletely = rule.IgnoreCompletely,
            IgnoreCollectionOrder = rule.IgnoreCollectionOrder,
        };

        ignoreRules.Add(newRule);

        logger.LogDebug(
            "Added rule for {PropertyPath} with settings: {Settings}",
            rule.PropertyPath,
            GetRuleSettingsDescription(rule));

        // Mark configuration as dirty to trigger rebuild
        MarkConfigurationDirty();

        // Invalidate cache when ignore rules change
        if (cacheService != null)
        {
            var newFingerprint = cacheService.GenerateConfigurationFingerprint(this);
            cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Add multiple ignore rules to the configuration in a batch operation
    /// This is more efficient than calling AddIgnoreRule multiple times as it only invalidates cache once.
    /// </summary>
    public void AddIgnoreRulesBatch(IEnumerable<IgnoreRule> rules)
    {
        if (rules == null || !rules.Any())
        {
            return;
        }

        var rulesList = rules.ToList();
        logger.LogDebug("Adding {Count} ignore rules in batch operation", rulesList.Count);

        foreach (var rule in rulesList)
        {
            if (rule == null)
            {
                continue;
            }

            // Remove existing rule for this property first
            RemoveIgnoredProperty(rule.PropertyPath);

            // Create a new rule with our logger to ensure proper logging
            var newRule = new IgnoreRule(logger)
            {
                PropertyPath = rule.PropertyPath,
                IgnoreCompletely = rule.IgnoreCompletely,
                IgnoreCollectionOrder = rule.IgnoreCollectionOrder,
            };

            ignoreRules.Add(newRule);
        }

        // Log summary instead of individual rules for performance
        var ignoreCompletelyCount = rulesList.Count(r => r.IgnoreCompletely);
        var ignoreCollectionOrderCount = rulesList.Count(r => r.IgnoreCollectionOrder);

        logger.LogDebug(
            "Batch operation completed: {TotalRules} rules added, {IgnoreCompletelyCount} ignore completely, {IgnoreCollectionOrderCount} ignore collection order",
            rulesList.Count,
            ignoreCompletelyCount,
            ignoreCollectionOrderCount);

        // Mark configuration as dirty to trigger rebuild
        MarkConfigurationDirty();

        // Single cache invalidation at the end for performance
        if (cacheService != null)
        {
            var newFingerprint = cacheService.GenerateConfigurationFingerprint(this);
            cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Get all currently ignored properties.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<string> GetIgnoredProperties() =>
        AllIgnoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();

    /// <summary>
    /// Get all ignore rules.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<IgnoreRule> GetIgnoreRules() => AllIgnoreRules.ToList();

    /// <summary>
    /// Clear all ignore rules.
    /// </summary>
    public void ClearIgnoreRules()
    {
        var count = ignoreRules.Count;
        ignoreRules.Clear();
        logger.LogInformation("Cleared {Count} ignore rules", count);

        // Mark configuration as dirty to trigger rebuild
        MarkConfigurationDirty();

        // Invalidate cache when ignore rules change
        if (cacheService != null)
        {
            var newFingerprint = cacheService.GenerateConfigurationFingerprint(this);
            cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Apply all configured settings from ignore rules.
    /// </summary>
    public void ApplyConfiguredSettings()
    {
        // Performance optimization: Use cached configuration if available and not dirty
        lock (configurationLock)
        {
            var currentFingerprint = GenerateConfigurationFingerprint();

            if (!isConfigurationDirty && string.Equals(lastConfigurationFingerprint, currentFingerprint, StringComparison.Ordinal) && cachedConfig != null)
            {
                // Configuration hasn't changed, apply cached config
                ApplyCachedConfiguration(cachedConfig);
                logger.LogDebug("Applied cached configuration (fingerprint: {Fingerprint})", currentFingerprint.Substring(0, 8));
                return;
            }

            // Configuration has changed or cache is invalid, rebuild
            logger.LogDebug(
                "Rebuilding comparison configuration (fingerprint changed from {Old} to {New})",
                lastConfigurationFingerprint.Substring(0, Math.Min(8, lastConfigurationFingerprint.Length)),
                currentFingerprint.Substring(0, 8));

            BuildAndCacheConfiguration();
            lastConfigurationFingerprint = currentFingerprint;
            isConfigurationDirty = false;
        }
    }

    /// <summary>
    /// Filter differences based on ignore rules with pattern matching support.
    /// </summary>
    /// <param name="result">The comparison result to filter.</param>
    /// <returns>A filtered comparison result.</returns>
    public ComparisonResult FilterIgnoredDifferences(ComparisonResult result)
    {
        if (result == null || !result.Differences.Any())
        {
            return result;
        }

        // CRITICAL FIX: Use the generated patterns from MembersToIgnore, not just the original rule paths
        // The patterns we generated (including System.Collections variations) are in compareLogic.Config.MembersToIgnore
        // THREAD SAFETY FIX: Create thread-safe copy to avoid concurrent modification exceptions
        HashSet<string> propertiesToIgnoreCompletely;
        lock (configurationLock)
        {
            var membersToIgnoreSnapshot = compareLogic.Config.MembersToIgnore?.ToList() ?? new List<string>();
            propertiesToIgnoreCompletely = new HashSet<string>(membersToIgnoreSnapshot, System.StringComparer.Ordinal);
        }

        // Also add the original rule paths for backward compatibility
        foreach (var rule in AllIgnoreRules.Where(r => r.IgnoreCompletely))
        {
            propertiesToIgnoreCompletely.Add(rule.PropertyPath);
        }

        if (!propertiesToIgnoreCompletely.Any())
        {
            return result;
        }

        // Performance optimization: Use different strategies based on rule count
        var useFastFiltering = ShouldUseFastFiltering();

        if (useFastFiltering)
        {
            return FilterDifferencesDirectly(result, propertiesToIgnoreCompletely);
        }
        else
        {
            return FilterDifferencesWithPatternMatching(result, propertiesToIgnoreCompletely);
        }
    }

    /// <summary>
    /// Add a smart ignore rule.
    /// </summary>
    public void AddSmartIgnoreRule(SmartIgnoreRule rule)
    {
        if (rule == null)
        {
            return;
        }

        // Remove existing rule with same type and value
        smartIgnoreRules.RemoveAll(r => r.Type == rule.Type && string.Equals(r.Value, rule.Value, StringComparison.Ordinal));

        smartIgnoreRules.Add(rule);
        logger.LogInformation("Added smart ignore rule: {Description}", rule.Description);
    }

    /// <summary>
    /// Remove a smart ignore rule.
    /// </summary>
    public void RemoveSmartIgnoreRule(SmartIgnoreRule rule)
    {
        if (rule == null)
        {
            return;
        }

        var removed = smartIgnoreRules.RemoveAll(r => r.Type == rule.Type && string.Equals(r.Value, rule.Value, StringComparison.Ordinal));
        if (removed > 0)
        {
            logger.LogInformation("Removed smart ignore rule: {Description}", rule.Description);
        }
    }

    /// <summary>
    /// Apply a preset of smart ignore rules.
    /// </summary>
    public void ApplySmartIgnorePreset(string presetName)
    {
        if (!SmartIgnorePresets.AllPresets.TryGetValue(presetName, out var preset))
        {
            logger.LogWarning("Unknown preset: {PresetName}", presetName);
            return;
        }

        foreach (var rule in preset)
        {
            AddSmartIgnoreRule(rule);
        }

        logger.LogInformation("Applied smart ignore preset '{PresetName}' with {Count} rules", presetName, preset.Count);
    }

    /// <summary>
    /// Clear all smart ignore rules.
    /// </summary>
    public void ClearSmartIgnoreRules()
    {
        var count = smartIgnoreRules.Count;
        smartIgnoreRules.Clear();
        logger.LogInformation("Cleared {Count} smart ignore rules", count);
    }

    /// <summary>
    /// Get all smart ignore rules.
    /// </summary>
    /// <returns></returns>
    public IReadOnlyList<SmartIgnoreRule> GetSmartIgnoreRules() => smartIgnoreRules.ToList();

    /// <summary>
    /// Filter differences using smart ignore rules.
    /// </summary>
    /// <returns></returns>
    public ComparisonResult FilterSmartIgnoredDifferences(ComparisonResult result, Type? modelType = null) => smartIgnoreProcessor.FilterResult(result, smartIgnoreRules, modelType);

    /// <summary>
    /// Normalize values of specified properties throughout an object graph.
    /// </summary>
    public void NormalizePropertyValues(object obj, List<string> propertyNames)
    {
        if (obj == null || propertyNames == null || !propertyNames.Any())
        {
            return;
        }

        try
        {
            // Use reflection to process the object graph
            ProcessObject(obj, propertyNames, new HashSet<object>());
            logger.LogDebug("Normalized {PropertyCount} properties in object", propertyNames.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error normalizing property values");
        }
    }

    /// <summary>
    /// Automatically adds properties with XmlIgnore attributes to the MembersToIgnore list
    /// This prevents properties that should not be serialized from being compared.
    /// </summary>
    public void AddXmlIgnorePropertiesToIgnoreList(Type modelType)
    {
        if (modelType == null)
        {
            return;
        }

        try
        {
            var ignoredProperties = FindXmlIgnoreProperties(modelType);
            if (!ignoredProperties.Any())
            {
                return;
            }

            var newRules = ignoredProperties
                .Select(p => new IgnoreRule(logger)
                {
                    PropertyPath = p,
                    IgnoreCompletely = true,
                })
                .ToList();

            xmlIgnoreRules.Clear();
            xmlIgnoreRules.AddRange(newRules);

            logger.LogInformation("Added {Count} XmlIgnore properties as ignore rules for type '{TypeName}'", ignoredProperties.Count, modelType.Name);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error adding XmlIgnore properties to ignore list for type '{TypeName}'", modelType.Name);
        }
    }

    private string GetRuleSettingsDescription(IgnoreRule rule)
    {
        var settings = new List<string>();
        if (rule.IgnoreCompletely)
        {
            settings.Add("IgnoreCompletely");
        }

        if (rule.IgnoreCollectionOrder)
        {
            settings.Add("IgnoreCollectionOrder");
        }

        return string.Join(", ", settings);
    }

    /// <summary>
    /// Generate a fingerprint of the current configuration for change detection.
    /// </summary>
    private string GenerateConfigurationFingerprint()
    {
        var config = new
        {
            GlobalIgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder,
            GlobalIgnoreStringCase = !compareLogic.Config.CaseSensitive,
            IgnoreRules = AllIgnoreRules
                .OrderBy(r => r.PropertyPath, System.StringComparer.Ordinal)
                .Select(r => new { r.PropertyPath, r.IgnoreCompletely, r.IgnoreCollectionOrder })
                .ToList(),
            SmartIgnoreRules = smartIgnoreRules
                .Where(r => r.IsEnabled)
                .OrderBy(r => r.Type).ThenBy(r => r.Value, System.StringComparer.Ordinal)
                .Select(r => new { r.Type, r.Value, r.IsEnabled })
                .ToList(),
        };

        var json = JsonSerializer.Serialize(config);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Apply a previously cached configuration.
    /// </summary>
    private void ApplyCachedConfiguration(CachedComparisonConfig cachedConfig)
    {
        // Copy cached settings to current config
        compareLogic.Config.IgnoreCollectionOrder = cachedConfig.IgnoreCollectionOrder;
        compareLogic.Config.CaseSensitive = !cachedConfig.CaseSensitive;

        // Clear and rebuild MembersToIgnore
        compareLogic.Config.MembersToIgnore.Clear();
        foreach (var member in cachedConfig.MembersToIgnore)
        {
            compareLogic.Config.MembersToIgnore.Add(member);
        }

        // Clear and rebuild CustomComparers
        compareLogic.Config.CustomComparers.Clear();
        foreach (var comparer in cachedConfig.CustomComparers)
        {
            compareLogic.Config.CustomComparers.Add(comparer);
        }
    }

    /// <summary>
    /// Performance check: if we have a lot of ignore rules for a large domain model,
    /// use fast property filtering instead of expensive pattern generation.
    /// </summary>
    private bool ShouldUseFastFiltering()
    {
        // TEMPORARILY DISABLED: Fast filtering caused infinite recursion
        // TODO: Implement proper fast filtering without recursion issues
        return false;

        // If we have many ignore rules, use fast filtering to avoid MembersToIgnore pattern explosion
        var ignoreCompletelyCount = AllIgnoreRules.Count(r => r.IgnoreCompletely);

        // Threshold: if more than 10 properties are being ignored completely, use fast filtering
        // For smaller numbers, the standard pattern matching is fine and more reliable
        return ignoreCompletelyCount > 10;
    }

    /// <summary>
    /// Build and cache configuration with performance optimization for large domain models.
    /// </summary>
    private void BuildAndCacheConfiguration()
    {
        try
        {
            // Store the current global ignore collection order setting
            var globalIgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder;
            logger.LogDebug("Building configuration with global IgnoreCollectionOrder: {GlobalSetting}", globalIgnoreCollectionOrder);

            // THREAD SAFETY: Use lock to prevent concurrent configuration changes
            lock (configurationLock)
            {
                // Clear any existing configuration - ensure collections are not null first
                if (compareLogic.Config.MembersToIgnore == null)
                {
                    compareLogic.Config.MembersToIgnore = new List<string>();
                }
                else
                {
                    compareLogic.Config.MembersToIgnore.Clear();
                }

                // Ensure other critical collections are initialized
                if (compareLogic.Config.CustomComparers == null)
                {
                    compareLogic.Config.CustomComparers = new List<BaseTypeComparer>();
                }
                else
                {
                    compareLogic.Config.CustomComparers.Clear();
                }

                if (compareLogic.Config.AttributesToIgnore == null)
                {
                    compareLogic.Config.AttributesToIgnore = new List<Type>();
                }

                if (compareLogic.Config.MembersToInclude == null)
                {
                    compareLogic.Config.MembersToInclude = new List<string>();
                }
            }

            // Performance optimization: Check if we should use fast filtering
            var useFastFiltering = ShouldUseFastFiltering();
            if (useFastFiltering)
            {
                logger.LogInformation(
                    "Using fast property filtering optimization for {Count} ignore rules (avoiding MembersToIgnore pattern explosion)",
                    AllIgnoreRules.Count(r => r.IgnoreCompletely));

                // Apply collection order rules
                ApplyCollectionOrderRulesOnly();

                // Add the fast property filter comparer
                var rootComparer = RootComparerFactory.GetRootComparer();
                var fastFilterComparer = new FastPropertyFilterComparer(rootComparer, AllIgnoreRules, logger);
                compareLogic.Config.CustomComparers.Insert(0, fastFilterComparer); // Insert at beginning for priority

                // Cache the configuration
                cachedConfig = new CachedComparisonConfig
                {
                    IgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder,
                    CaseSensitive = compareLogic.Config.CaseSensitive,
                    MembersToIgnore = new List<string>(compareLogic.Config.MembersToIgnore),
                    CustomComparers = new List<BaseTypeComparer>(compareLogic.Config.CustomComparers),
                };

                logger.LogInformation(
                    "Direct filtering configuration cached: {IgnoreCount} ignore patterns, {ComparerCount} custom comparers",
                    compareLogic.Config.MembersToIgnore.Count,
                    compareLogic.Config.CustomComparers.Count);
                return;
            }

            // Original pattern generation logic for smaller rule sets
            ApplyAllRulesWithPatternGeneration(globalIgnoreCollectionOrder);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in BuildAndCacheConfiguration");
        }
    }

    /// <summary>
    /// Apply only collection order rules without expensive pattern generation.
    /// </summary>
    private void ApplyCollectionOrderRulesOnly()
    {
        // Check all the rules to make sure we have both Results and RelatedItems
        var resultsRule = AllIgnoreRules.Any(r => r.PropertyPath.Contains("Results") && r.IgnoreCollectionOrder);
        var relatedItemsRule = AllIgnoreRules.Any(r => r.PropertyPath.Contains("RelatedItems") && r.IgnoreCollectionOrder);

        logger.LogDebug(
            "Rules check - Have Results rule: {HasResults}, Have RelatedItems rule: {HasRelatedItems}",
            resultsRule,
            relatedItemsRule);

        // Remove any existing custom comparers of our type to avoid duplicates
        var removedCount = compareLogic.Config.CustomComparers.RemoveAll(c => c is PropertySpecificCollectionOrderComparer);
        if (removedCount > 0)
        {
            logger.LogDebug("Removed {RemovedCount} existing PropertySpecificCollectionOrderComparer instances", removedCount);
        }

        // Get all properties that need to ignore collection order
        var propertiesWithIgnoreOrder = AllIgnoreRules
            .Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();

        logger.LogDebug("Found {Count} properties with ignore collection order setting", propertiesWithIgnoreOrder.Count);

        // If we have properties that need to ignore collection order
        if (propertiesWithIgnoreOrder.Any())
        {
            try
            {
                // Create property-specific collection order comparer
                var expandedProperties = propertiesWithIgnoreOrder
                    .SelectMany(p => new[] { p, p.Replace("[*]", "[0]"), p.Replace("[*]", "[1]") })
                    .Distinct(System.StringComparer.Ordinal)
                    .ToList();

                var rootComparer = RootComparerFactory.GetRootComparer();
                var collectionOrderComparer = new PropertySpecificCollectionOrderComparer(rootComparer, expandedProperties, logger);

                var newComparerList = new List<BaseTypeComparer>(compareLogic.Config.CustomComparers)
                {
                    collectionOrderComparer,
                };

                compareLogic.Config.CustomComparers = newComparerList;

                logger.LogDebug("Added property-specific collection order comparer for {PropertyCount} properties", expandedProperties.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating PropertySpecificCollectionOrderComparer");
            }
        }

        // Apply smart ignore rules to configuration
        smartIgnoreProcessor.ApplyRulesToConfig(smartIgnoreRules, compareLogic.Config);
    }

    /// <summary>
    /// Original pattern generation logic for smaller rule sets.
    /// </summary>
    private void ApplyAllRulesWithPatternGeneration(bool globalIgnoreCollectionOrder)
    {
        // Check all the rules to make sure we have both Results and RelatedItems
        var resultsRule = AllIgnoreRules.Any(r => r.PropertyPath.Contains("Results") && r.IgnoreCollectionOrder);
        var relatedItemsRule = AllIgnoreRules.Any(r => r.PropertyPath.Contains("RelatedItems") && r.IgnoreCollectionOrder);

        logger.LogDebug(
            "Rules check - Have Results rule: {HasResults}, Have RelatedItems rule: {HasRelatedItems}",
            resultsRule,
            relatedItemsRule);

        // Remove any existing custom comparers of our type to avoid duplicates
        var removedCount = compareLogic.Config.CustomComparers.RemoveAll(c => c is PropertySpecificCollectionOrderComparer);
        if (removedCount > 0)
        {
            logger.LogDebug("Removed {RemovedCount} existing PropertySpecificCollectionOrderComparer instances", removedCount);
        }

        // Get all properties that need to ignore collection order
        var propertiesWithIgnoreOrder = AllIgnoreRules
            .Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();

        logger.LogDebug("Found {Count} properties with ignore collection order setting", propertiesWithIgnoreOrder.Count);

        // If we have properties that need to ignore collection order
        if (propertiesWithIgnoreOrder.Any())
        {
            try
            {
                // Create property-specific collection order comparer
                var expandedProperties = propertiesWithIgnoreOrder
                    .SelectMany(p => new[] { p, p.Replace("[*]", "[0]"), p.Replace("[*]", "[1]") })
                    .Distinct(System.StringComparer.Ordinal)
                    .ToList();

                var rootComparer = RootComparerFactory.GetRootComparer();
                var collectionOrderComparer = new PropertySpecificCollectionOrderComparer(rootComparer, expandedProperties, logger);

                var newComparerList = new List<BaseTypeComparer>(compareLogic.Config.CustomComparers)
                {
                    collectionOrderComparer,
                };

                compareLogic.Config.CustomComparers = newComparerList;

                logger.LogDebug("Added property-specific collection order comparer for {PropertyCount} properties", expandedProperties.Count);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error creating PropertySpecificCollectionOrderComparer");
            }
        }
        else
        {
            // No property-specific collection order rules, so preserve the global setting
            compareLogic.Config.IgnoreCollectionOrder = globalIgnoreCollectionOrder;
            logger.LogDebug("No property-specific collection order rules found. Preserving global IgnoreCollectionOrder setting: {GlobalSetting}", globalIgnoreCollectionOrder);
        }

        // Apply smart ignore rules to configuration
        smartIgnoreProcessor.ApplyRulesToConfig(smartIgnoreRules, compareLogic.Config);

        // Apply rules with optimized pattern generation
        var rulesApplied = 0;
        var beforeCount = compareLogic.Config.MembersToIgnore.Count;

        foreach (var rule in AllIgnoreRules)
        {
            try
            {
                rule.ApplyTo(compareLogic.Config);
                rulesApplied++;

                // Safety check: if we're generating too many ignore patterns, warn and stop
                // Reasonable limit
                var currentCount = compareLogic.Config.MembersToIgnore.Count;
                if (currentCount > 1000)
                {
                    logger.LogWarning("Ignore pattern limit reached ({CurrentCount} patterns). Stopping rule application to maintain performance.", currentCount);
                    break;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error applying rule for property {PropertyPath}", rule.PropertyPath);
            }
        }

        var finalCount = compareLogic.Config.MembersToIgnore.Count;
        var generatedPatterns = finalCount - beforeCount;

        // Cache the configuration
        cachedConfig = new CachedComparisonConfig
        {
            IgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder,
            CaseSensitive = compareLogic.Config.CaseSensitive,
            MembersToIgnore = new List<string>(compareLogic.Config.MembersToIgnore),
            CustomComparers = new List<BaseTypeComparer>(compareLogic.Config.CustomComparers),
        };

        // Only log configuration summary in debug mode to reduce noise
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Configuration built and cached: {RuleCount} rules applied, {IgnoreCount} ignore patterns, {ComparerCount} custom comparers",
                rulesApplied,
                finalCount,
                compareLogic.Config.CustomComparers.Count);
        }
    }

    /// <summary>
    /// Mark configuration as dirty when rules change.
    /// </summary>
    private void MarkConfigurationDirty()
    {
        lock (configurationLock)
        {
            isConfigurationDirty = true;
            cachedConfig = null;
        }
    }

    /// <summary>
    /// Fast direct filtering for large rule sets - avoids expensive pattern matching.
    /// </summary>
    private ComparisonResult FilterDifferencesDirectly(ComparisonResult result, HashSet<string> propertiesToIgnore)
    {
        var originalCount = result.Differences.Count;
        var filteredDifferences = new List<Difference>();
        var ignoredCount = 0;

        // Performance optimization: Pre-build normalized patterns for fast lookup
        var normalizedPatterns = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var wildcardPatterns = new List<string>();

        foreach (var property in propertiesToIgnore)
        {
            normalizedPatterns.Add(property);

            // Handle collection patterns
            if (property.Contains("[*]"))
            {
                wildcardPatterns.Add(property);
            }
            else
            {
                // Add common collection variations for simple properties
                normalizedPatterns.Add($"Results[*].{property}");
                normalizedPatterns.Add($"Items[*].{property}");
                normalizedPatterns.Add($"Data[*].{property}");
            }
        }

        logger.LogInformation("Using direct filtering for {IgnoreCount} ignore patterns (performance optimized)", propertiesToIgnore.Count);

        foreach (var difference in result.Differences)
        {
            var shouldIgnore = false;
            var propertyPath = difference.PropertyName;

            // Fast exact match check
            if (normalizedPatterns.Contains(propertyPath))
            {
                shouldIgnore = true;
            }
            else
            {
                // Fast wildcard pattern check (only for patterns containing [*])
                foreach (var pattern in wildcardPatterns)
                {
                    if (DoesPropertyMatchWildcardPattern(propertyPath, pattern))
                    {
                        shouldIgnore = true;
                        break;
                    }
                }
            }

            if (!shouldIgnore)
            {
                filteredDifferences.Add(difference);
            }
            else
            {
                ignoredCount++;
            }
        }

        // THREAD SAFETY FIX: Lock when modifying the result collection
        lock (result.Differences)
        {
            result.Differences.Clear();
            result.Differences.AddRange(filteredDifferences);
        }

        if (ignoredCount > 0)
        {
            logger.LogInformation(
                "Direct filtering removed {IgnoredCount} differences from {OriginalCount} total (kept {FilteredCount})",
                ignoredCount,
                originalCount,
                filteredDifferences.Count);
        }

        return result;
    }

    /// <summary>
    /// Original pattern matching approach for smaller rule sets.
    /// </summary>
    private ComparisonResult FilterDifferencesWithPatternMatching(ComparisonResult result, HashSet<string> propertiesToIgnore)
    {
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug(
                "Filtering differences using {Count} tree navigator ignore patterns",
                propertiesToIgnore.Count);
        }

        var originalCount = result.Differences.Count;
        var filteredDifferences = new List<Difference>();
        var ignoredCount = 0;

        // Performance: Only log detailed differences in debug mode to avoid overhead
        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("=== ALL DIFFERENCES FOUND BY COMPARENETOBJECTS ===");
            foreach (var diff in result.Differences)
            {
                logger.LogDebug(
                    "DIFFERENCE: '{PropertyName}' | Value1: '{Value1}' | Value2: '{Value2}'",
                    diff.PropertyName,
                    diff.Object1Value,
                    diff.Object2Value);
            }

            logger.LogDebug("=== END OF ALL DIFFERENCES ===");
        }

        // THREAD SAFETY FIX: Use try-catch with multiple attempts to handle concurrent access
        var differencesToProcess = new List<Difference>();
        var maxRetries = 3;
        var retryCount = 0;
        var copySuccessful = false;

        while (retryCount < maxRetries && !copySuccessful)
        {
            try
            {
                // Attempt to create a safe copy of the differences collection
                differencesToProcess.Clear();
                foreach (var diff in result.Differences)
                {
                    differencesToProcess.Add(diff);
                }

                copySuccessful = true; // Success, exit retry loop
            }
            catch (InvalidOperationException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    // Last resort: return original result without filtering
                    logger.LogError("Failed to create thread-safe copy of differences after {MaxRetries} attempts. Skipping pattern filtering.", maxRetries);
                    return result;
                }

                // Brief delay before retry
                Thread.Sleep(10);
            }
            catch (ArgumentException)
            {
                retryCount++;
                if (retryCount >= maxRetries)
                {
                    // Last resort: return original result without filtering
                    logger.LogError("Failed to create thread-safe copy of differences after {MaxRetries} attempts. Skipping pattern filtering.", maxRetries);
                    return result;
                }

                // Brief delay before retry
                Thread.Sleep(10);
            }
        }

        if (!copySuccessful)
        {
            logger.LogError("Failed to create thread-safe copy of differences. Skipping pattern filtering.");
            return result;
        }

        foreach (var difference in differencesToProcess)
        {
            var shouldIgnore = PropertyIgnoreHelper.ShouldIgnoreProperty(difference.PropertyName, propertiesToIgnore, logger);

            // Also check the static MembersToIgnore list (for System.Collections variations)
            if (!shouldIgnore)
            {
                // THREAD SAFETY FIX: Use lock to safely access shared configuration
                List<string> membersToIgnoreSnapshot;
                lock (configurationLock)
                {
                    membersToIgnoreSnapshot = compareLogic.Config.MembersToIgnore?.ToList() ?? new List<string>();
                }

                foreach (var ignoredPath in membersToIgnoreSnapshot)
                {
                    if (difference.PropertyName.Equals(ignoredPath, StringComparison.OrdinalIgnoreCase) ||
                        difference.PropertyName.StartsWith(ignoredPath + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldIgnore = true;

                        // Performance: Only log filtering details in trace mode
                        if (logger.IsEnabled(LogLevel.Trace))
                        {
                            logger.LogTrace(
                                "Filtering out property '{PropertyPath}' (matched static MembersToIgnore: '{MatchedPath}')",
                                difference.PropertyName,
                                ignoredPath);
                        }

                        break;
                    }
                }
            }

            if (!shouldIgnore)
            {
                if (logger.IsEnabled(LogLevel.Trace))
                {
                    logger.LogTrace(
                        "Property '{PropertyName}' DOES NOT MATCH any ignore pattern - WILL BE KEPT AS DIFFERENCE",
                        difference.PropertyName);
                }

                filteredDifferences.Add(difference);
            }
            else
            {
                // Only log in debug mode to prevent spam in production with large comparisons
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug(
                        "Property '{PropertyName}' MATCHED ignore pattern - WILL BE IGNORED",
                        difference.PropertyName);
                }

                ignoredCount++;
            }
        }

        // THREAD SAFETY FIX: Lock when modifying the result collection
        lock (result.Differences)
        {
            result.Differences.Clear();
            result.Differences.AddRange(filteredDifferences);
        }

        // Only log filtering summary if significant differences were filtered and Information level is enabled
        if (ignoredCount > 0 && logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation(
                "Pattern matching filtering removed {IgnoredCount} differences from {OriginalCount} total (kept {FilteredCount})",
                ignoredCount,
                originalCount,
                filteredDifferences.Count);
        }

        // Log cache performance for monitoring
        var (hits, misses, hitRatio) = PropertyIgnoreHelper.GetCacheStats();
        if (hits + misses > 0)
        {
            logger.LogDebug(
                "Pattern matching cache performance: {Hits} hits, {Misses} misses, {HitRatio:P1} hit ratio",
                hits,
                misses,
                hitRatio);
        }

        return result;
    }

    /// <summary>
    /// Fast wildcard pattern matching without regex.
    /// </summary>
    private bool DoesPropertyMatchWildcardPattern(string propertyPath, string pattern)
    {
        if (!pattern.Contains("[*]"))
        {
            return false;
        }

        // Simple pattern matching for [*] - replace with any digits
        var patternParts = pattern.Split(new[] { "[*]" }, StringSplitOptions.None);
        if (patternParts.Length != 2)
        {
            return false;
        }

        var prefix = patternParts[0];
        var suffix = patternParts[1];

        if (!propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!string.IsNullOrEmpty(suffix) && !propertyPath.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Check that there's a collection index between prefix and suffix
        var middle = propertyPath.Substring(prefix.Length, propertyPath.Length - prefix.Length - suffix.Length);
        return middle.StartsWith("[", StringComparison.Ordinal) && middle.Contains("]");
    }

    /// <summary>
    /// Process an object to normalize specified properties.
    /// </summary>
    private void ProcessObject(object obj, List<string> propertyNames, HashSet<object> processedObjects)
    {
        // Avoid cycles in the object graph
        if (obj == null || !obj.GetType().IsClass || obj is string || processedObjects.Contains(obj))
        {
            return;
        }

        processedObjects.Add(obj);

        var type = obj.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if this property should be normalized
            if (propertyNames.Contains(property.Name, System.StringComparer.Ordinal) && property.CanWrite)
            {
                SetDefaultValue(obj, property);
            }
            else if (property.CanRead)
            {
                var value = property.GetValue(obj);

                // If it's a collection, process each item
                if (value is System.Collections.IEnumerable enumerable && !(value is string))
                {
                    foreach (var item in enumerable)
                    {
                        ProcessObject(item, propertyNames, processedObjects);
                    }
                }

                // If it's a complex object, process it recursively
                else if (value != null && value.GetType().IsClass && !(value is string))
                {
                    ProcessObject(value, propertyNames, processedObjects);
                }
            }
        }
    }

    /// <summary>
    /// Set a property to its default/normalized value.
    /// </summary>
    private void SetDefaultValue(object obj, PropertyInfo property)
    {
        if (!property.CanWrite)
        {
            return;
        }

        object defaultValue = null;

        var propertyType = property.PropertyType;

        if (propertyType == typeof(string))
        {
            defaultValue = string.Empty;
        }
        else if (propertyType == typeof(int) || propertyType == typeof(int?))
        {
            defaultValue = 0;
        }
        else if (propertyType == typeof(double) || propertyType == typeof(double?))
        {
            defaultValue = 0.0;
        }
        else if (propertyType == typeof(decimal) || propertyType == typeof(decimal?))
        {
            defaultValue = 0m;
        }
        else if (propertyType == typeof(bool) || propertyType == typeof(bool?))
        {
            defaultValue = false;
        }
        else if (propertyType == typeof(DateTime) || propertyType == typeof(DateTime?))
        {
            defaultValue = DateTime.MinValue;
        }
        else if (propertyType == typeof(Guid) || propertyType == typeof(Guid?))
        {
            defaultValue = Guid.Empty;
        }
        else if (propertyType.IsEnum)
        {
            // Use the first enum value
            defaultValue = Enum.GetValues(propertyType).Cast<object>().FirstOrDefault();
        }

        try
        {
            property.SetValue(obj, defaultValue);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to set default value for property {PropertyName} of type {PropertyType}",
                property.Name,
                propertyType.Name);
        }
    }

    /// <summary>
    /// Recursively finds all properties with XmlIgnore attributes in a type and its nested types.
    /// </summary>
    private List<string> FindXmlIgnoreProperties(Type type, string currentPath = "", HashSet<Type>? processedTypes = null)
    {
        var ignoredProperties = new List<string>();

        if (processedTypes == null)
        {
            processedTypes = new HashSet<Type>();
        }

        // Prevent infinite recursion on circular references
        if (processedTypes.Contains(type))
        {
            return ignoredProperties;
        }

        processedTypes.Add(type);

        try
        {
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                var propertyPath = string.IsNullOrEmpty(currentPath) ? property.Name : $"{currentPath}.{property.Name}";

                // Check if this property has XmlIgnore attribute
                if (property.GetCustomAttribute<XmlIgnoreAttribute>() != null)
                {
                    ignoredProperties.Add(propertyPath);
                    logger.LogTrace("Found XmlIgnore property: {PropertyPath}", propertyPath);
                }
                else
                {
                    // Recursively check nested types (but not primitive types, strings, etc.)
                    var propertyType = property.PropertyType;
                    if (!propertyType.IsPrimitive &&
                        propertyType != typeof(string) &&
                        propertyType != typeof(DateTime) &&
                        propertyType != typeof(decimal) &&
                        !propertyType.IsEnum &&
                        !propertyType.IsValueType)
                    {
                        // Handle collections
                        if (propertyType.IsGenericType &&
                            propertyType.GetGenericTypeDefinition() == typeof(List<>))
                        {
                            var elementType = propertyType.GetGenericArguments()[0];
                            if (!elementType.IsPrimitive && elementType != typeof(string))
                            {
                                var nestedProperties = FindXmlIgnoreProperties(elementType, $"{propertyPath}[*]", processedTypes);
                                ignoredProperties.AddRange(nestedProperties);
                            }
                        }
                        else if (propertyType.IsArray)
                        {
                            var elementType = propertyType.GetElementType();
                            if (!elementType.IsPrimitive && elementType != typeof(string))
                            {
                                var nestedProperties = FindXmlIgnoreProperties(elementType, $"{propertyPath}[*]", processedTypes);
                                ignoredProperties.AddRange(nestedProperties);
                            }
                        }
                        else
                        {
                            // Regular nested object
                            var nestedProperties = FindXmlIgnoreProperties(propertyType, propertyPath, processedTypes);
                            ignoredProperties.AddRange(nestedProperties);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error scanning properties for XmlIgnore attributes in type '{TypeName}'", type.Name);
        }

        return ignoredProperties;
    }

    /// <summary>
    /// Custom class to hold comparison configuration for caching.
    /// </summary>
    private class CachedComparisonConfig
    {
        public bool IgnoreCollectionOrder
        {
            get; set;
        }

        public bool CaseSensitive
        {
            get; set;
        }

        public List<string> MembersToIgnore { get; set; } = new List<string>();

        public List<BaseTypeComparer> CustomComparers { get; set; } = new List<BaseTypeComparer>();
    }

    /// <summary>
    /// High-performance property filter comparer that bypasses MembersToIgnore pattern matching
    /// for large domain models with many ignore rules.
    /// </summary>
    private class FastPropertyFilterComparer : BaseTypeComparer
    {
        private readonly HashSet<string> exactIgnorePaths;
        private readonly HashSet<string> collectionPrefixes;
        private readonly ILogger logger;

        public FastPropertyFilterComparer(
            RootComparer rootComparer,
            IEnumerable<IgnoreRule> ignoreRules,
            ILogger logger)
            : base(rootComparer)
        {
            this.logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;

            // Build fast lookup structures
            exactIgnorePaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            collectionPrefixes = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var rule in ignoreRules.Where(r => r.IgnoreCompletely))
            {
                var path = rule.PropertyPath;
                exactIgnorePaths.Add(path);

                // Handle collection patterns efficiently
                if (path.Contains("[*]"))
                {
                    // Store the prefix before [*] for fast prefix matching
                    var prefixEnd = path.IndexOf("[*]", StringComparison.Ordinal);
                    if (prefixEnd > 0)
                    {
                        var prefix = path.Substring(0, prefixEnd);
                        collectionPrefixes.Add(prefix);
                    }
                }
            }

            this.logger.LogInformation(
                "FastPropertyFilterComparer initialized with {ExactPaths} exact paths and {CollectionPrefixes} collection prefixes",
                exactIgnorePaths.Count,
                collectionPrefixes.Count);
        }

        public override bool IsTypeMatch(Type type1, Type type2) =>
            // Handle all property comparisons to filter at the right level
            true;

        public override void CompareType(CompareParms parms)
        {
            // Fast check if this property should be ignored
            if (ShouldIgnoreProperty(parms.BreadCrumb))
            {
                // Skip this property entirely - no difference will be recorded
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Fast filter: Ignoring property '{Path}'", parms.BreadCrumb);
                }

                return;
            }

            // Property should NOT be ignored, so continue with normal comparison
            // Use the root comparer to do the actual comparison
            RootComparer.Compare(parms);
        }

        private bool ShouldIgnoreProperty(string propertyPath)
        {
            if (string.IsNullOrEmpty(propertyPath))
            {
                return false;
            }

            // Fast exact match check
            if (exactIgnorePaths.Contains(propertyPath))
            {
                return true;
            }

            // Fast collection pattern check
            foreach (var prefix in collectionPrefixes)
            {
                if (propertyPath.StartsWith(prefix + "[", StringComparison.OrdinalIgnoreCase))
                {
                    // Check if it matches the pattern (prefix[number].rest or prefix[number])
                    var afterPrefix = propertyPath.Substring(prefix.Length);
                    if (afterPrefix.StartsWith("[", StringComparison.Ordinal) &&
                        (afterPrefix.Contains("].") || System.Text.RegularExpressions.Regex.IsMatch(afterPrefix, @"^\[\d+\]($|\.)", RegexOptions.None, RegexTimeout)))
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
