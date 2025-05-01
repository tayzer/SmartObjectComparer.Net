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

    public ComparisonConfigurationService(
        ILogger<ComparisonConfigurationService> logger,
        IOptions<ComparisonConfigurationOptions> options = null)
    {
        this.logger = logger;

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
            // Make sure global ignore collection order is OFF so our specific rules work
            compareLogic.Config.IgnoreCollectionOrder = false;
            logger.LogWarning("Setting global IgnoreCollectionOrder to FALSE to ensure property-specific rules work");
            
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
                    
                    // Turn off the global ignore collection order to ensure our per-property comparer works
                    var savedIgnoreOrder = compareLogic.Config.IgnoreCollectionOrder;
                    if (savedIgnoreOrder) 
                    {
                        compareLogic.Config.IgnoreCollectionOrder = false;
                        logger.LogWarning("Disabled global IgnoreCollectionOrder setting (was: {Original}) to ensure property-specific comparison works", 
                            savedIgnoreOrder);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error creating PropertySpecificCollectionOrderComparer");
                }
            }

            // Apply all rules
            int rulesApplied = 0;
            foreach (var rule in ignoreRules)
            {
                try 
                {
                    rule.ApplyTo(compareLogic.Config);
                    rulesApplied++;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error applying rule for property {PropertyPath}", rule.PropertyPath);
                }
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