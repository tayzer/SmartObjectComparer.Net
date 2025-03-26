using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.V2;

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