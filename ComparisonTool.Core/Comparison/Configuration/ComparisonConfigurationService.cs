using System.Reflection;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;

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
            
        // For debugging: output the current list of all rules
        var rulesWithIgnoreOrder = ignoreRules
            .Where(r => r.IgnoreCollectionOrder)
            .Select(r => r.PropertyPath)
            .ToList();
            
        logger.LogWarning("Current collection order ignore rules: {Rules}", 
            string.Join(", ", rulesWithIgnoreOrder));
            
        // Invalidate cache when ignore rules change
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
        try
        {
            // Store the current global ignore collection order setting 
            var globalIgnoreCollectionOrder = compareLogic.Config.IgnoreCollectionOrder;
            logger.LogWarning("Current global IgnoreCollectionOrder setting: {GlobalSetting}", globalIgnoreCollectionOrder);
            
            // Clear any existing configuration
            compareLogic.Config.MembersToIgnore.Clear();
            
            logger.LogDebug("Cleared existing members to ignore");
            
            // Check all the rules to make sure we have both Results and RelatedItems
            var resultsRule = ignoreRules.Any(r => r.PropertyPath.Contains("Results") && r.IgnoreCollectionOrder);
            var relatedItemsRule = ignoreRules.Any(r => r.PropertyPath.Contains("RelatedItems") && r.IgnoreCollectionOrder);
            
            logger.LogWarning("Rules check - Have Results rule: {HasResults}, Have RelatedItems rule: {HasRelatedItems}",
                resultsRule, relatedItemsRule);
            
            // Remove any existing custom comparers of our type to avoid duplicates
            var removedCount = compareLogic.Config.CustomComparers.RemoveAll(c => c is PropertySpecificCollectionOrderComparer);
            
            logger.LogWarning("Removed {RemovedCount} existing PropertySpecificCollectionOrderComparer instances", removedCount);
            
            // Get all properties that need to ignore collection order
            var propertiesWithIgnoreOrder = ignoreRules
                .Where(r => r.IgnoreCollectionOrder && !r.IgnoreCompletely)
                .Select(r => r.PropertyPath)
                .ToList();
                
            logger.LogWarning("Found {Count} properties with ignore collection order setting: {Properties}", 
                propertiesWithIgnoreOrder.Count, 
                string.Join(", ", propertiesWithIgnoreOrder));
            
            // Also dump existing comparers for debugging
            var comparerTypes = compareLogic.Config.CustomComparers.Select(c => c.GetType().Name).ToList();
            logger.LogWarning("Current custom comparers: {ComparerTypes}", string.Join(", ", comparerTypes));
            
            // If we have properties that need to ignore collection order
            if (propertiesWithIgnoreOrder.Any())
            {
                try
                {
                    // We have property-specific collection order rules, so we need to disable global
                    // collection order ignoring and use our custom comparer instead
                    compareLogic.Config.IgnoreCollectionOrder = false;
                    logger.LogWarning("Disabled global IgnoreCollectionOrder to enable property-specific collection order rules");
                    
                    // Get the current comparers to inspect
                    var originalComparers = compareLogic.Config.CustomComparers.ToList();
                    
                    // Get a root comparer using the factory
                    var rootComparer = RootComparerFactory.GetRootComparer();
                    
                    logger.LogWarning("Successfully created RootComparer from factory");
                    
                    // Make a copy of the property paths with wildcards to ensure matching works properly
                    var expandedProperties = new List<string>(propertiesWithIgnoreOrder);
                    
                    // Add wildcard versions if not already present
                    foreach (var property in propertiesWithIgnoreOrder.ToList()) // Use ToList to avoid modifying during iteration
                    {
                        // Add wildcarded versions of collection paths
                        if (property.Contains("Results") && !property.Contains("[*]"))
                        {
                            // Add with wildcard if it doesn't already have one
                            string wildcardPath = property.Contains("[") 
                                ? Regex.Replace(property, @"\[\d+\]", "[*]") 
                                : property + "[*]";
                            
                            if (!expandedProperties.Contains(wildcardPath))
                            {
                                expandedProperties.Add(wildcardPath);
                                logger.LogWarning("Added wildcard version: {WildcardPath}", wildcardPath);
                            }
                        }
                        
                        if (property.Contains("RelatedItems") && !property.Contains("[*]"))
                        {
                            // Add with wildcard if it doesn't already have one
                            string wildcardPath = property.Contains("[") 
                                ? Regex.Replace(property, @"\[\d+\]", "[*]") 
                                : property + "[*]";
                            
                            if (!expandedProperties.Contains(wildcardPath))
                            {
                                expandedProperties.Add(wildcardPath);
                                logger.LogWarning("Added wildcard version: {WildcardPath}", wildcardPath);
                            }
                        }
                    }
                    
                    // Create our custom comparer to handle property-specific collection ordering
                    var propertySpecificComparer = new PropertySpecificCollectionOrderComparer(
                        rootComparer, 
                        expandedProperties,
                        logger);
                        
                    // Important: Ensure our comparer is the FIRST comparer in the list so it runs before others
                    // Create a new list with our comparer first, then all original comparers
                    var newComparerList = new List<BaseTypeComparer>
                    {
                        propertySpecificComparer // Our comparer is first
                    };
                    
                    // Add all the existing comparers (excluding any previous instances of our comparer)
                    newComparerList.AddRange(originalComparers.Where(c => !(c is PropertySpecificCollectionOrderComparer)));
                    
                    // Set the new list of comparers
                    compareLogic.Config.CustomComparers = newComparerList;
                        
                    logger.LogWarning("Added property-specific collection order comparer at the BEGINNING of the custom comparers list for {PropertyCount} properties", 
                        expandedProperties.Count);
                    
                    // Verify our comparer is first
                    var firstComparerName = compareLogic.Config.CustomComparers.FirstOrDefault()?.GetType().Name ?? "none";
                    logger.LogWarning("First comparer is now: {FirstComparer}", firstComparerName);
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
                logger.LogWarning("No property-specific collection order rules found. Preserving global IgnoreCollectionOrder setting: {GlobalSetting}", globalIgnoreCollectionOrder);
            }

            // Apply smart ignore rules to configuration
            smartIgnoreProcessor.ApplyRulesToConfig(smartIgnoreRules, compareLogic.Config);

            // Handle property ignore rules - we'll enhance the MembersToIgnore with our custom logic
            // by overriding the comparison result processing rather than using a custom comparer

            // Apply rules with safety limits to prevent performance issues
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
                        logger.LogWarning("Ignore pattern limit reached ({CurrentCount} patterns). Stopping rule application to maintain performance. Consider more specific ignore rules.", currentCount);
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
            
            if (generatedPatterns > 500)
            {
                logger.LogWarning("Generated {GeneratedPatterns} ignore patterns from {RulesApplied} rules. This may impact performance.", generatedPatterns, rulesApplied);
            }
            
            logger.LogWarning("Applied configuration settings with {RuleCount} rules. MembersToIgnore: {IgnoreCount}, CustomComparers: {ComparerCount}",
                rulesApplied,
                compareLogic.Config.MembersToIgnore.Count,
                compareLogic.Config.CustomComparers.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in ApplyConfiguredSettings");
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

        logger.LogInformation("Filtering differences using {Count} tree navigator ignore patterns: {Patterns}", 
            propertiesToIgnoreCompletely.Count, string.Join(", ", propertiesToIgnoreCompletely));

        var originalCount = result.Differences.Count;
        var filteredDifferences = new List<Difference>();

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
                        logger.LogInformation("FILTERING OUT difference for ignored property: {PropertyPath} (matched static MembersToIgnore: {MatchedPath})", 
                            difference.PropertyName, ignoredPath);
                        break;
                    }
                }
            }
            
            if (!shouldIgnore)
            {
                filteredDifferences.Add(difference);
                logger.LogDebug("KEEPING difference for property: {PropertyPath}", difference.PropertyName);
            }
            else if (!compareLogic.Config.MembersToIgnore.Contains(difference.PropertyName))
            {
                logger.LogInformation("FILTERING OUT difference for ignored property: {PropertyPath} (matched tree navigator rule)", difference.PropertyName);
            }
        }

        result.Differences.Clear();
        result.Differences.AddRange(filteredDifferences);

        var filteredCount = result.Differences.Count;
        var removedCount = originalCount - filteredCount;

        if (removedCount > 0)
        {
            logger.LogInformation("Tree Navigator filtering removed {RemovedCount} differences from {OriginalCount} total (kept {FilteredCount})", 
                removedCount, originalCount, filteredCount);
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