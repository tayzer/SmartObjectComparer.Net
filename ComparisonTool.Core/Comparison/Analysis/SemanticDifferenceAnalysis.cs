namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Extension to ComparisonPatternAnalysis to include semantic grouping
/// </summary>
public class SemanticDifferenceAnalysis
{
    /// <summary>
    /// Groups of semantically related differences
    /// </summary>
    public List<SemanticDifferenceGroup> SemanticGroups { get; set; } = new List<SemanticDifferenceGroup>();

    /// <summary>
    /// The overall analysis this semantic grouping is based on
    /// </summary>
    public ComparisonPatternAnalysis BaseAnalysis { get; set; }

    /// <summary>
    /// Total number of differences analyzed
    /// </summary>
    public int TotalDifferences => BaseAnalysis?.TotalDifferences ?? 0;

    /// <summary>
    /// Total number of differences categorized in semantic groups
    /// </summary>
    public int CategorizedDifferences => SemanticGroups.Sum(g => g.DifferenceCount);

    /// <summary>
    /// Percentage of differences that have been semantically categorized
    /// </summary>
    public double CategorizedPercentage => TotalDifferences > 0
        ? (double)CategorizedDifferences / TotalDifferences * 100
        : 0;
}