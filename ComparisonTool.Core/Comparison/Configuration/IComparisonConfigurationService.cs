using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Configuration;

/// <summary>
/// Extension methods for Comparison Configuration.
/// </summary>
public static class ComparisonConfigurationExtensions
{
    /// <summary>
    /// Creates a deep clone of the <see cref="ComparisonConfig"/>.
    /// </summary>
    /// <param name="config"></param>
    /// <returns></returns>
    public static ComparisonConfig Clone(this ComparisonConfig config)
    {
        var clone = new ComparisonConfig()
        {
            MaxDifferences = config.MaxDifferences,
            IgnoreObjectTypes = config.IgnoreObjectTypes,
            ComparePrivateFields = config.ComparePrivateFields,
            ComparePrivateProperties = config.ComparePrivateProperties,
            CompareReadOnly = config.CompareReadOnly,
            IgnoreCollectionOrder = config.IgnoreCollectionOrder,
            CaseSensitive = config.CaseSensitive
        };

        foreach (var variable in config.MembersToIgnore)
        {
            clone.MembersToIgnore.Add(variable);
        }

        return clone;
    }
}

/// <summary>
/// Interface for comparison configuration management
/// </summary>
public interface IComparisonConfigurationService
{
    /// <summary>
    /// Get the current comparison configuration
    /// </summary>
    ComparisonConfig GetCurrentConfig();

    /// <summary>
    /// Get the compare logic instance
    /// </summary>
    CompareLogic GetCompareLogic();

    /// <summary>
    /// Configure whether to ignore collection order
    /// </summary>
    void SetIgnoreCollectionOrder(bool ignoreOrder);

    /// <summary>
    /// Get whether collection order is being ignored
    /// </summary>
    bool GetIgnoreCollectionOrder();

    /// <summary>
    /// Configure whether to ignore string case
    /// </summary>
    void SetIgnoreStringCase(bool ignoreCase);

    /// <summary>
    /// Get whether string case is being ignored
    /// </summary>
    bool GetIgnoreStringCase();

    /// <summary>
    /// Configure the comparer to ignore specific properties
    /// </summary>
    void IgnoreProperty(string propertyPath);

    /// <summary>
    /// Remove a property from the ignore list
    /// </summary>
    void RemoveIgnoredProperty(string propertyPath);

    /// <summary>
    /// Add an ignore rule to the configuration
    /// </summary>
    void AddIgnoreRule(IgnoreRule rule);

    /// <summary>
    /// Get all currently ignored properties
    /// </summary>
    IReadOnlyList<string> GetIgnoredProperties();

    /// <summary>
    /// Get all ignore rules
    /// </summary>
    IReadOnlyList<IgnoreRule> GetIgnoreRules();

    /// <summary>
    /// Apply all configured settings from ignore rules
    /// </summary>
    void ApplyConfiguredSettings();

    void NormalizePropertyValues(object obj, List<string> propertyNames);
}