namespace ComparisonTool.Core.Comparison.Configuration;

/// <summary>
/// Options for the comparison configuration service.
/// </summary>
public class ComparisonConfigurationOptions
{
    public int MaxDifferences { get; set; } = 100;

    public bool DefaultIgnoreCollectionOrder { get; set; } = false;

    public bool DefaultIgnoreStringCase { get; set; } = false;
}
