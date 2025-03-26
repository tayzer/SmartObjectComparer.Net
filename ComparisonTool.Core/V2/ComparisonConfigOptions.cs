namespace ComparisonTool.Core.V2;

/// <summary>
/// Options for the comparison configuration service
/// </summary>
public class ComparisonConfigOptions
{
    public int MaxDifferences { get; set; } = 100;
    public bool DefaultIgnoreCollectionOrder { get; set; } = false;
    public bool DefaultIgnoreStringCase { get; set; } = false;
}