// <copyright file="SemanticDifferenceAnalysis.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Extension to ComparisonPatternAnalysis to include semantic grouping.
/// </summary>
public class SemanticDifferenceAnalysis
{
    /// <summary>
    /// Gets or sets groups of semantically related differences.
    /// </summary>
    public List<SemanticDifferenceGroup> SemanticGroups { get; set; } = new List<SemanticDifferenceGroup>();

    /// <summary>
    /// Gets or sets the overall analysis this semantic grouping is based on.
    /// </summary>
    required public ComparisonPatternAnalysis BaseAnalysis
    {
        get; set;
    }

    /// <summary>
    /// Gets total number of differences analyzed.
    /// </summary>
    public int TotalDifferences => BaseAnalysis?.TotalDifferences ?? 0;

    /// <summary>
    /// Gets total number of differences categorized in semantic groups.
    /// </summary>
    public int CategorizedDifferences => SemanticGroups.Sum(g => g.DifferenceCount);

    /// <summary>
    /// Gets percentage of differences that have been semantically categorized.
    /// </summary>
    public double CategorizedPercentage => TotalDifferences > 0
        ? (double)CategorizedDifferences / TotalDifferences * 100
        : 0;
}
