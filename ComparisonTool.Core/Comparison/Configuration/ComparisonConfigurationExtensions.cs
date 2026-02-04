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
            CaseSensitive = config.CaseSensitive,
        };

        foreach (var variable in config.MembersToIgnore)
        {
            clone.MembersToIgnore.Add(variable);
        }

        return clone;
    }
}
