using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Configuration;

/// <summary>
/// Interface for comparison configuration management.
/// </summary>
public interface IComparisonConfigurationService
{
    /// <summary>
    /// Get the current comparison configuration.
    /// </summary>
    /// <returns></returns>
    ComparisonConfig GetCurrentConfig();

    /// <summary>
    /// Get the compare logic instance.
    /// </summary>
    /// <returns></returns>
    CompareLogic GetCompareLogic();

    /// <summary>
    /// Get a thread-safe isolated CompareLogic instance for concurrent operations
    /// This prevents collection modification exceptions when multiple comparisons run in parallel.
    /// </summary>
    /// <returns></returns>
    CompareLogic GetThreadSafeCompareLogic();

    /// <summary>
    /// Configure whether to ignore collection order.
    /// </summary>
    void SetIgnoreCollectionOrder(bool ignoreOrder);

    /// <summary>
    /// Get whether collection order is being ignored.
    /// </summary>
    /// <returns></returns>
    bool GetIgnoreCollectionOrder();

    /// <summary>
    /// Configure whether to ignore string case.
    /// </summary>
    void SetIgnoreStringCase(bool ignoreCase);

    /// <summary>
    /// Get whether string case is being ignored.
    /// </summary>
    /// <returns></returns>
    bool GetIgnoreStringCase();

    /// <summary>
    /// Configure the comparer to ignore specific properties.
    /// </summary>
    void IgnoreProperty(string propertyPath);

    /// <summary>
    /// Remove a property from the ignore list.
    /// </summary>
    void RemoveIgnoredProperty(string propertyPath);

    /// <summary>
    /// Add an ignore rule to the configuration.
    /// </summary>
    void AddIgnoreRule(IgnoreRule rule);

    /// <summary>
    /// Add multiple ignore rules to the configuration in a batch operation
    /// This is more efficient than calling AddIgnoreRule multiple times.
    /// </summary>
    void AddIgnoreRulesBatch(IEnumerable<IgnoreRule> rules);

    /// <summary>
    /// Get all currently ignored properties.
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<string> GetIgnoredProperties();

    /// <summary>
    /// Get all ignore rules.
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<IgnoreRule> GetIgnoreRules();

    /// <summary>
    /// Get ignore rules that should be visible to users (excludes auto rules like XmlIgnore).
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<IgnoreRule> GetUserIgnoreRules();

    /// <summary>
    /// Clear all ignore rules.
    /// </summary>
    void ClearIgnoreRules();

    /// <summary>
    /// Apply all configured settings from ignore rules.
    /// </summary>
    void ApplyConfiguredSettings();

    /// <summary>
    /// Filter differences based on ignore rules with pattern matching support.
    /// </summary>
    /// <returns></returns>
    ComparisonResult FilterIgnoredDifferences(ComparisonResult result);

    /// <summary>
    /// Add a smart ignore rule.
    /// </summary>
    void AddSmartIgnoreRule(SmartIgnoreRule rule);

    /// <summary>
    /// Remove a smart ignore rule.
    /// </summary>
    void RemoveSmartIgnoreRule(SmartIgnoreRule rule);

    /// <summary>
    /// Apply a preset of smart ignore rules.
    /// </summary>
    void ApplySmartIgnorePreset(string presetName);

    /// <summary>
    /// Clear all smart ignore rules.
    /// </summary>
    void ClearSmartIgnoreRules();

    /// <summary>
    /// Get all smart ignore rules.
    /// </summary>
    /// <returns></returns>
    IReadOnlyList<SmartIgnoreRule> GetSmartIgnoreRules();

    /// <summary>
    /// Filter differences using smart ignore rules.
    /// </summary>
    /// <returns></returns>
    ComparisonResult FilterSmartIgnoredDifferences(ComparisonResult result, Type modelType = null);

    void NormalizePropertyValues(object obj, List<string> propertyNames);

    /// <summary>
    /// Automatically add properties with XmlIgnore attributes to the ignore list.
    /// </summary>
    void AddXmlIgnorePropertiesToIgnoreList(Type modelType);

    /// <summary>
    /// Set the cache service for configuration change invalidation.
    /// </summary>
    void SetCacheService(ComparisonResultCacheService cacheService);
}
