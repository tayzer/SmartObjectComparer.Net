// <copyright file="ComparisonPatternAnalysis.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

public class ComparisonPatternAnalysis
{
    public int TotalFilesPaired
    {
        get; set;
    }

    public int FilesWithDifferences
    {
        get; set;
    }

    public int TotalDifferences
    {
        get; set;
    }

    // Common path patterns across files
    public IList<GlobalPatternInfo> CommonPathPatterns { get; set; } = new List<GlobalPatternInfo>();

    // Common property changes across files
    public IList<GlobalPropertyChangeInfo> CommonPropertyChanges { get; set; } = new List<GlobalPropertyChangeInfo>();

    // Common category statistics across files
    public IDictionary<DifferenceCategory, int> TotalByCategory { get; set; } = new Dictionary<DifferenceCategory, int>();

    // Files grouped by similarity
    public IList<SimilarFileGroup> SimilarFileGroups { get; set; } = new List<SimilarFileGroup>();
}
