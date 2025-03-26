using System.Reflection;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
        var rule = new IgnoreRule
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

        RemoveIgnoredProperty(rule.PropertyPath);

        ignoreRules.Add(rule);
        logger.LogDebug("Added rule for {PropertyPath} with settings: {Settings}",
            rule.PropertyPath,
            GetRuleSettingsDescription(rule));
    }

    private string GetRuleSettingsDescription(IgnoreRule rule)
    {
        var settings = new List<string>();
        if (rule.IgnoreCompletely) settings.Add("IgnoreCompletely");
        if (rule.IgnoreCollectionOrder) settings.Add("IgnoreCollectionOrder");
        if (rule.IgnoreCase) settings.Add("IgnoreCase");
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
        compareLogic.Config.MembersToIgnore.Clear();

        foreach (var rule in ignoreRules)
        {
            rule.ApplyTo(compareLogic.Config);
        }

        logger.LogInformation("Applied configuration settings with {RuleCount} rules", ignoreRules.Count);
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