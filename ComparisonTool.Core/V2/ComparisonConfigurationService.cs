using System.Reflection;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ComparisonTool.Core.V2;

/// <summary>
/// Service responsible for managing configuration settings for object comparison
/// </summary>
public class ComparisonConfigurationService : IComparisonConfigurationService
{
    private readonly ILogger<ComparisonConfigurationService> _logger;
    private readonly CompareLogic _compareLogic;
    private readonly List<IgnoreRule> _ignoreRules = new();

    public ComparisonConfigurationService(
        ILogger<ComparisonConfigurationService> logger,
        IOptions<ComparisonConfigOptions> options = null)
    {
        _logger = logger;

        var configOptions = options?.Value ?? new ComparisonConfigOptions();

        _compareLogic = new CompareLogic
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

        _logger.LogInformation("Initialized ComparisonConfigurationService with MaxDifferences={MaxDiffs}, " +
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
        return _compareLogic.Config;
    }

    /// <summary>
    /// Get the compare logic instance
    /// </summary>
    public CompareLogic GetCompareLogic()
    {
        return _compareLogic;
    }

    /// <summary>
    /// Configure whether to ignore collection order
    /// </summary>
    public void SetIgnoreCollectionOrder(bool ignoreOrder)
    {
        _compareLogic.Config.IgnoreCollectionOrder = ignoreOrder;
        _logger.LogDebug("Set IgnoreCollectionOrder to {Value}", ignoreOrder);
    }

    /// <summary>
    /// Get whether collection order is being ignored
    /// </summary>
    public bool GetIgnoreCollectionOrder()
    {
        return _compareLogic.Config.IgnoreCollectionOrder;
    }

    /// <summary>
    /// Configure whether to ignore string case
    /// </summary>
    public void SetIgnoreStringCase(bool ignoreCase)
    {
        _compareLogic.Config.CaseSensitive = !ignoreCase;
        _logger.LogDebug("Set CaseSensitive to {Value} (IgnoreCase={IgnoreCase})", !ignoreCase, ignoreCase);
    }

    /// <summary>
    /// Get whether string case is being ignored
    /// </summary>
    public bool GetIgnoreStringCase()
    {
        return !_compareLogic.Config.CaseSensitive;
    }

    /// <summary>
    /// Configure the comparer to ignore specific properties
    /// </summary>
    public void IgnoreProperty(string propertyPath)
    {
        // Add to our ignore rules
        var rule = new IgnoreRule
        {
            PropertyPath = propertyPath,
            IgnoreCompletely = true
        };

        _ignoreRules.Add(rule);
        _logger.LogDebug("Added property {PropertyPath} to ignore list", propertyPath);
    }

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    public void RemoveIgnoredProperty(string propertyPath)
    {
        // Remove from our rules
        var rulesToRemove = _ignoreRules
            .Where(r => r.PropertyPath == propertyPath)
            .ToList();

        foreach (var rule in rulesToRemove)
        {
            _ignoreRules.Remove(rule);
        }

        _logger.LogDebug("Removed property {PropertyPath} from ignore list", propertyPath);
    }

    /// <summary>
    /// Add an ignore rule to the configuration
    /// </summary>
    public void AddIgnoreRule(IgnoreRule rule)
    {
        if (rule == null) return;

        // Remove any existing rule for this property
        RemoveIgnoredProperty(rule.PropertyPath);

        // Add the new rule
        _ignoreRules.Add(rule);
        _logger.LogDebug("Added rule for {PropertyPath} with settings: {Settings}",
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
        return _ignoreRules
            .Where(r => r.IgnoreCompletely)
            .Select(r => r.PropertyPath)
            .ToList();
    }

    /// <summary>
    /// Get all ignore rules
    /// </summary>
    public IReadOnlyList<IgnoreRule> GetIgnoreRules()
    {
        return _ignoreRules.ToList();
    }

    /// <summary>
    /// Apply all configured settings from ignore rules
    /// </summary>
    public void ApplyConfiguredSettings()
    {
        // Clear existing config
        _compareLogic.Config.MembersToIgnore.Clear();

        // Apply all ignore rules
        foreach (var rule in _ignoreRules)
        {
            rule.ApplyTo(_compareLogic.Config);
        }

        _logger.LogInformation("Applied configuration settings with {RuleCount} rules", _ignoreRules.Count);
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
            _logger.LogDebug("Normalized {PropertyCount} properties in object", propertyNames.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error normalizing property values");
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

        // Process all properties of the object
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            // Check if this property should be normalized
            if (propertyNames.Contains(property.Name) && property.CanWrite)
            {
                // Set to default value based on property type
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

        // Handle different property types
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

        // Set the property to its default value
        try
        {
            property.SetValue(obj, defaultValue);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to set default value for property {PropertyName} of type {PropertyType}",
                property.Name, propertyType.Name);
        }
    }
}