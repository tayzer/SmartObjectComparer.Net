using System.Reflection;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using System.Text.Json;

namespace ComparisonTool.Core.Comparison.Configuration;

/// <summary>
/// Service responsible for managing configuration settings for object comparison
/// </summary>
public class ComparisonConfigurationService : IComparisonConfigurationService
{
    private readonly ILogger<ComparisonConfigurationService> logger;
    private readonly CompareLogic compareLogic;
    private readonly List<IgnoreRule> ignoreRules = new();
    private readonly List<SmartIgnoreRule> smartIgnoreRules = new();
    private readonly SmartIgnoreProcessor smartIgnoreProcessor;
    private ComparisonResultCacheService? _cacheService;

    // Performance optimization: Cache compiled configuration to avoid rebuilding for every file
    private bool _isConfigurationDirty = true;
    private string _lastConfigurationFingerprint = string.Empty;
    private readonly object _configurationLock = new object();
    private ComparisonConfig? _cachedConfig = null;

    public ComparisonConfigurationService(
        ILogger<ComparisonConfigurationService> logger,
        IOptions<ComparisonConfigurationOptions> options = null)
    {
        this.logger = logger;
        this.smartIgnoreProcessor = new SmartIgnoreProcessor(logger);

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
                CaseSensitive = !configOptions.DefaultIgnoreStringCase
            }
        };

        this.logger.LogInformation("Initialized ComparisonConfigurationService with MaxDifferences={MaxDiffs}, " +
                                   "IgnoreCollectionOrder={IgnoreOrder}, IgnoreCase={IgnoreCase}",
            configOptions.MaxDifferences,
            configOptions.DefaultIgnoreCollectionOrder,
            configOptions.DefaultIgnoreStringCase);
    }

    /// <summary>
    /// Set the cache service for invalidating cached results when configuration changes
    /// </summary>
    public void SetCacheService(ComparisonResultCacheService cacheService)
    {
        _cacheService = cacheService;
    }

    /// <summary>
    /// Get the current comparison configuration
    /// </summary>
    public ComparisonConfig GetCurrentConfig()
    {
        return compareLogic.Config;
    }

    /// <summary>
    /// Get the compare logic instance
    /// </summary>
    public CompareLogic GetCompareLogic()
    {
        return compareLogic;
    }

    /// <summary>
    /// Configure whether to ignore collection order
    /// </summary>
    public void SetIgnoreCollectionOrder(bool ignoreOrder)
    {
        compareLogic.Config.IgnoreCollectionOrder = ignoreOrder;
        MarkConfigurationDirty();
        logger.LogDebug("Set IgnoreCollectionOrder to {Value}", ignoreOrder);
        
        // Invalidate cache when configuration changes
        if (_cacheService != null)
        {
            var newFingerprint = _cacheService.GenerateConfigurationFingerprint(this);
            _cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Get whether collection order is being ignored
    /// </summary>
    public bool GetIgnoreCollectionOrder()
    {
        return compareLogic.Config.IgnoreCollectionOrder;
    }

    /// <summary>
    /// Configure whether to ignore string case
    /// </summary>
    public void SetIgnoreStringCase(bool ignoreCase)
    {
        compareLogic.Config.CaseSensitive = !ignoreCase;
        MarkConfigurationDirty();
        logger.LogDebug("Set CaseSensitive to {Value} (IgnoreCase={IgnoreCase})", !ignoreCase, ignoreCase);
        
        // Invalidate cache when configuration changes
        if (_cacheService != null)
        {
            var newFingerprint = _cacheService.GenerateConfigurationFingerprint(this);
            _cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Get whether string case is being ignored
    /// </summary>
    public bool GetIgnoreStringCase()
    {
        return !compareLogic.Config.CaseSensitive;
    }

    /// <summary>
    /// Configure the comparer to ignore specific properties
    /// </summary>
    public void IgnoreProperty(string propertyPath)
    {
        var rule = new IgnoreRule(logger)
        {
            PropertyPath = propertyPath,
            IgnoreCompletely = true
        };

        ignoreRules.Add(rule);
        logger.LogDebug("Added property {PropertyPath} to ignore list", propertyPath);
    }

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        var rulesToRemove = ignoreRules
            .Where(r => r.PropertyPath == propertyPath)
            .ToList();

        foreach (var rule in rulesToRemove)
        {
            ignoreRules.Remove(rule);
        }

        logger.LogDebug("Removed property {PropertyPath} from ignore list", propertyPath);
    }

    /// <summary>
    /// Add an ignore rule to the configuration
    /// </summary>
    public void AddIgnoreRule(IgnoreRule rule)
    {
        if (rule == null) return;

        // Log the rule being added
        logger.LogWarning("Adding rule for {PropertyPath} with settings: IgnoreCompletely={IgnoreCompletely}, IgnoreCollectionOrder={IgnoreOrder}",
            rule.PropertyPath,
            rule.IgnoreCompletely,
            rule.IgnoreCollectionOrder);

        RemoveIgnoredProperty(rule.PropertyPath);

        // Create a new rule with our logger to ensure proper logging
        var newRule = new IgnoreRule(logger)
        {
            PropertyPath = rule.PropertyPath,
            IgnoreCompletely = rule.IgnoreCompletely,
            IgnoreCollectionOrder = rule.IgnoreCollectionOrder
        };
        
        ignoreRules.Add(newRule);
        
        logger.LogDebug("Added rule for {PropertyPath} with settings: {Settings}",
            rule.PropertyPath,
            GetRuleSettingsDescription(rule));
            
        // Mark configuration as dirty to trigger rebuild
        MarkConfigurationDirty();
            
        // Invalidate cache when ignore rules change
        if (_cacheService != null)
        {
            var newFingerprint = _cacheService.GenerateConfigurationFingerprint(this);
            _cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    /// <summary>
    /// Add multiple ignore rules to the configuration in a batch operation
    /// This is more efficient than calling AddIgnoreRule multiple times as it only invalidates cache once
    /// </summary>
    public void AddIgnoreRulesBatch(IEnumerable<IgnoreRule> rules)
    {
        if (rules == null || !rules.Any()) return;

        var rulesList = rules.ToList();
        logger.LogInformation("Adding {Count} ignore rules in batch operation", rulesList.Count);

        foreach (var rule in rulesList)
        {
            if (rule == null) continue;

            // Remove existing rule for this property first
            RemoveIgnoredProperty(rule.PropertyPath);

            // Create a new rule with our logger to ensure proper logging
            var newRule = new IgnoreRule(logger)
            {
                PropertyPath = rule.PropertyPath,
                IgnoreCompletely = rule.IgnoreCompletely,
                IgnoreCollectionOrder = rule.IgnoreCollectionOrder
            };
            
            ignoreRules.Add(newRule);
        }

        // Log summary instead of individual rules for performance
        var ignoreCompletelyCount = rulesList.Count(r => r.IgnoreCompletely);
        var ignoreCollectionOrderCount = rulesList.Count(r => r.IgnoreCollectionOrder);
        
        logger.LogInformation("Batch operation completed: {TotalRules} rules added, {IgnoreCompletelyCount} ignore completely, {IgnoreCollectionOrderCount} ignore collection order",
            rulesList.Count, ignoreCompletelyCount, ignoreCollectionOrderCount);

        // Mark configuration as dirty to trigger rebuild
        MarkConfigurationDirty();

        // Single cache invalidation at the end for performance
        if (_cacheService != null)
        {
            var newFingerprint = _cacheService.GenerateConfigurationFingerprint(this);
            _cacheService.InvalidateConfigurationChanges(newFingerprint);
        }
    }

    private string GetRuleSettingsDescription(IgnoreRule rule)
    {
        var settings = new List<string>();
        if (rule.IgnoreCompletely) settings.Add("IgnoreCompletely");
        if (rule.IgnoreCollectionOrder) settings.Add("IgnoreCollectionOrder");
        return string.Join(", ", settings);
    }

    /// <summary>
    /// Get all currently ignored properties
    /// </summary>
    public IReadOnlyList<string> GetIgnoredProperties()
    {
        return ignoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();
    }

    /// <summary>
    /// Get all ignore rules
    /// </summary>
    public IReadOnlyList<IgnoreRule> GetIgnoreRules()
    {
        return ignoreRules.ToList();
    }

    /// <summary>
    /// Apply all configured settings from ignore rules
    /// </summary>
    public void ApplyConfiguredSettings()
    {
        // Performance optimization: Use cached configuration if available and not dirty
        lock (_configurationLock)
        {
            var currentFingerprint = GenerateConfigurationFingerprint();
            
            if (!_isConfigurationDirty && _lastConfigurationFingerprint == currentFingerprint && _cachedConfig != null)
            {
                // Configuration hasn't changed, apply cached config
                ApplyCachedConfiguration(_cachedConfig);
                logger.LogDebug("Applied cached configuration (fingerprint: {Fingerprint})", currentFingerprint.Substring(0, 8));
                return;
            }

            // Configuration has changed or cache is invalid, rebuild
            logger.LogInformation("Rebuilding comparison configuration (fingerprint changed from {Old} to {New})", 
                _lastConfigurationFingerprint.Substring(0, Math.Min(8, _lastConfigurationFingerprint.Length)), 
                currentFingerprint.Substring(0, 8));
                
            BuildAndCacheConfiguration();
            _lastConfigurationFingerprint = currentFingerprint;
            _isConfigurationDirty = false;
        }
    }

    /// <summary>
    /// Generate a fingerprint of the current configuration for change detection
    /// </summary>
    private string GenerateConfigurationFingerprint()
    {
        var config = new
        {
            GlobalIgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder,
            GlobalIgnoreStringCase = !compareLogic.Config.CaseSensitive,
            IgnoreRules = ignoreRules
                .OrderBy(r => r.PropertyPath)
                .Select(r => new { r.PropertyPath, r.IgnoreCompletely, r.IgnoreCollectionOrder })
                .ToList(),
            SmartIgnoreRules = smartIgnoreRules
                .Where(r => r.IsEnabled)
                .OrderBy(r => r.Type).ThenBy(r => r.Value)
                .Select(r => new { r.Type, r.Value, r.IsEnabled })
                .ToList()
        };

        var json = JsonSerializer.Serialize(config);
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(json));
        return Convert.ToBase64String(hash);
    }

    /// <summary>
    /// Apply a previously cached configuration
    /// </summary>
    private void ApplyCachedConfiguration(ComparisonConfig cachedConfig)
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
    /// Build and cache the complete configuration (the expensive operation)
    /// </summary>
    private void BuildAndCacheConfiguration()
    {
        try
        {
            // Store the current global ignore collection order setting 
            var globalIgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder;
            logger.LogDebug("Building configuration with global IgnoreCollectionOrder: {GlobalSetting}", globalIgnoreCollectionOrder);
            
            // Clear any existing configuration
            compareLogic.Config.MembersToIgnore.Clear();
            
            // Check all the rules to make sure we have both Results and RelatedItems
            var resultsRule = ignoreRules.Any(r => r.PropertyPath.Contains("Results") && r.IgnoreCollectionOrder);
            var relatedItemsRule = ignoreRules.Any(r => r.PropertyPath.Contains("RelatedItems") && r.IgnoreCollectionOrder);
            
            logger.LogDebug("Rules check - Have Results rule: {HasResults}, Have RelatedItems rule: {HasRelatedItems}",
                resultsRule, relatedItemsRule);
            
            // Remove any existing custom comparers of our type to avoid duplicates
            var removedCount = compareLogic.Config.CustomComparers.RemoveAll(c => c is PropertySpecificCollectionOrderComparer);
            if (removedCount > 0)
            {
                logger.LogDebug("Removed {RemovedCount} existing PropertySpecificCollectionOrderComparer instances", removedCount);
            }
            
            // Get all properties that need to ignore collection order
            var propertiesWithIgnoreOrder = ignoreRules
                .Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely)
                .Select(r => r.PropertyPath)
                .ToList();
                
            logger.LogDebug("Found {Count} properties with ignore collection order setting", propertiesWithIgnoreOrder.Count);
            
            // If we have properties that need to ignore collection order
            if (propertiesWithIgnoreOrder.Any())
            {
                try
                {
                    // We have property-specific collection order rules, so we need to disable global
                    // collection order ignoring and use our custom comparer instead
                    compareLogic.Config.IgnoreCollectionOrder = false;
                    logger.LogDebug("Disabled global IgnoreCollectionOrder to enable property-specific collection order rules");
                    
                    // Get the current comparers to inspect
                    var originalComparers = compareLogic.Config.CustomComparers.ToList();
                    
                    // Get a root comparer using the factory
                    var rootComparer = RootComparerFactory.GetRootComparer();
                    
                    // Make a copy of the property paths with wildcards to ensure matching works properly
                    var expandedProperties = new List<string>(propertiesWithIgnoreOrder);
                    
                    // Add wildcard versions if not already present (optimized)
                    AddOptimizedWildcardVersions(expandedProperties, propertiesWithIgnoreOrder);
                    
                    // Create our custom comparer to handle property-specific collection ordering
                    var propertySpecificComparer = new PropertySpecificCollectionOrderComparer(
                        rootComparer, 
                        expandedProperties,
                        logger);
                        
                    // Important: Ensure our comparer is the FIRST comparer in the list so it runs before others
                    var newComparerList = new List<BaseTypeComparer> { propertySpecificComparer };
                    newComparerList.AddRange(originalComparers.Where(c => !(c is PropertySpecificCollectionOrderComparer)));
                    
                    // Set the new list of comparers
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
            int rulesApplied = 0;
            int beforeCount = compareLogic.Config.MembersToIgnore.Count;
            
            foreach (var rule in ignoreRules)
            {
                try 
                {
                    rule.ApplyTo(compareLogic.Config);
                    rulesApplied++;
                    
                    // Safety check: if we're generating too many ignore patterns, warn and stop
                    int currentCount = compareLogic.Config.MembersToIgnore.Count;
                    if (currentCount > 1000) // Reasonable limit
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
            
            int finalCount = compareLogic.Config.MembersToIgnore.Count;
            int generatedPatterns = finalCount - beforeCount;
            
            // Cache the configuration
            _cachedConfig = new ComparisonConfig
            {
                IgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder,
                CaseSensitive = compareLogic.Config.CaseSensitive,
                MembersToIgnore = new List<string>(compareLogic.Config.MembersToIgnore),
                CustomComparers = new List<BaseTypeComparer>(compareLogic.Config.CustomComparers)
            };
            
            logger.LogInformation("Configuration built and cached: {RuleCount} rules applied, {IgnoreCount} ignore patterns, {ComparerCount} custom comparers",
                rulesApplied, finalCount, compareLogic.Config.CustomComparers.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in BuildAndCacheConfiguration");
        }
    }

    /// <summary>
    /// Optimized method to add wildcard versions without excessive pattern generation
    /// </summary>
    private void AddOptimizedWildcardVersions(List<string> expandedProperties, List<string> originalProperties)
    {
        foreach (var property in originalProperties.ToList())
        {
            if (property.Contains("Results") && !property.Contains("[*]"))
            {
                string wildcardPath = property.Contains("[") 
                    ? Regex.Replace(property, @"\[\d+\]", "[*]") 
                    : property + "[*]";
                
                if (!expandedProperties.Contains(wildcardPath))
                {
                    expandedProperties.Add(wildcardPath);
                }
            }
            
            if (property.Contains("RelatedItems") && !property.Contains("[*]"))
            {
                string wildcardPath = property.Contains("[") 
                    ? Regex.Replace(property, @"\[\d+\]", "[*]") 
                    : property + "[*]";
                
                if (!expandedProperties.Contains(wildcardPath))
                {
                    expandedProperties.Add(wildcardPath);
                }
            }
        }
    }

    /// <summary>
    /// Mark configuration as dirty when rules change
    /// </summary>
    private void MarkConfigurationDirty()
    {
        lock (_configurationLock)
        {
            _isConfigurationDirty = true;
            _cachedConfig = null;
        }
    }

    /// <summary>
    /// Filter differences based on ignore rules with pattern matching support
    /// </summary>
    /// <param name="result">The comparison result to filter</param>
    /// <returns>A filtered comparison result</returns>
    public ComparisonResult FilterIgnoredDifferences(ComparisonResult result)
    {
        if (result == null || !result.Differences.Any())
            return result;

        var propertiesToIgnoreCompletely = ignoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToHashSet();

        if (!propertiesToIgnoreCompletely.Any())
            return result;

        if (logger.IsEnabled(LogLevel.Debug))
        {
            logger.LogDebug("Filtering differences using {Count} tree navigator ignore patterns", 
                propertiesToIgnoreCompletely.Count);
        }

        var originalCount = result.Differences.Count;
        var filteredDifferences = new List<Difference>();
        var ignoredCount = 0;

        foreach (var difference in result.Differences)
        {
            bool shouldIgnore = PropertyIgnoreHelper.ShouldIgnoreProperty(difference.PropertyName, propertiesToIgnoreCompletely, logger);
            
            // Also check the static MembersToIgnore list (for System.Collections variations)
            // Check both exact matches and if the difference is a sub-property of any ignored path
            if (!shouldIgnore)
            {
                foreach (var ignoredPath in compareLogic.Config.MembersToIgnore)
                {
                    if (difference.PropertyName.Equals(ignoredPath, StringComparison.OrdinalIgnoreCase) ||
                        difference.PropertyName.StartsWith(ignoredPath + ".", StringComparison.OrdinalIgnoreCase))
                    {
                        shouldIgnore = true;
                        if (logger.IsEnabled(LogLevel.Debug))
                        {
                            logger.LogDebug("Filtering out property '{PropertyPath}' (matched static MembersToIgnore: '{MatchedPath}')", 
                                difference.PropertyName, ignoredPath);
                        }
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

        result.Differences.Clear();
        result.Differences.AddRange(filteredDifferences);

        var filteredCount = result.Differences.Count;

        if (ignoredCount > 0)
        {
            logger.LogInformation("Ignore rule filtering removed {IgnoredCount} differences from {OriginalCount} total (kept {FilteredCount})", 
                ignoredCount, originalCount, filteredCount);
        }

        // Log cache performance for monitoring
        var (hits, misses, hitRatio) = PropertyIgnoreHelper.GetCacheStats();
        if (hits + misses > 0)
        {
            logger.LogDebug("Pattern matching cache performance: {Hits} hits, {Misses} misses, {HitRatio:P1} hit ratio", 
                hits, misses, hitRatio);
        }

        return result;
    }

    /// <summary>
    /// Add a smart ignore rule
    /// </summary>
    public void AddSmartIgnoreRule(SmartIgnoreRule rule)
    {
        if (rule == null) return;

        // Remove existing rule with same type and value
        smartIgnoreRules.RemoveAll(r => r.Type == rule.Type && r.Value == rule.Value);
        
        smartIgnoreRules.Add(rule);
        logger.LogInformation("Added smart ignore rule: {Description}", rule.Description);
    }

    /// <summary>
    /// Remove a smart ignore rule
    /// </summary>
    public void RemoveSmartIgnoreRule(SmartIgnoreRule rule)
    {
        if (rule == null) return;

        var removed = smartIgnoreRules.RemoveAll(r => r.Type == rule.Type && r.Value == rule.Value);
        if (removed > 0)
        {
            logger.LogInformation("Removed smart ignore rule: {Description}", rule.Description);
        }
    }

    /// <summary>
    /// Apply a preset of smart ignore rules
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
    /// Clear all smart ignore rules
    /// </summary>
    public void ClearSmartIgnoreRules()
    {
        var count = smartIgnoreRules.Count;
        smartIgnoreRules.Clear();
        logger.LogInformation("Cleared {Count} smart ignore rules", count);
    }

    /// <summary>
    /// Get all smart ignore rules
    /// </summary>
    public IReadOnlyList<SmartIgnoreRule> GetSmartIgnoreRules()
    {
        return smartIgnoreRules.ToList();
    }

    /// <summary>
    /// Filter differences using smart ignore rules
    /// </summary>
    public ComparisonResult FilterSmartIgnoredDifferences(ComparisonResult result, Type modelType = null)
    {
        return smartIgnoreProcessor.FilterResult(result, smartIgnoreRules, modelType);
    }

    /// <summary>
    /// Normalize values of specified properties throughout an object graph
    /// </summary>
    public void NormalizePropertyValues(object obj, List<string> propertyNames)
    {
        if (obj == null || propertyNames == null || !propertyNames.Any())
            return;

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
    /// Process an object to normalize specified properties
    /// </summary>
    private void ProcessObject(object obj, List<string> propertyNames, HashSet<object> processedObjects)
    {
        // Avoid cycles in the object graph
        if (obj == null || !obj.GetType().IsClass || obj is string || processedObjects.Contains(obj))
            return;

        processedObjects.Add(obj);

        var type = obj.GetType();

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if this property should be normalized
            if (propertyNames.Contains(property.Name) && property.CanWrite)
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
    /// Set a property to its default/normalized value
    /// </summary>
    private void SetDefaultValue(object obj, PropertyInfo property)
    {
        if (!property.CanWrite)
            return;

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
            logger.LogWarning(ex, "Failed to set default value for property {PropertyName} of type {PropertyType}",
                property.Name, propertyType.Name);
        }
    }
}