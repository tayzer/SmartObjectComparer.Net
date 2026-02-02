namespace ComparisonTool.Core.Comparison.Analysis;

public class ComparisonPatternAnalysis {
    public int TotalFilesPaired {
        get; set;
    }

    public int FilesWithDifferences {
        get; set;
    }

    public int TotalDifferences {
        get; set;
    }

    // Common path patterns across files
    public List<GlobalPatternInfo> CommonPathPatterns { get; set; } = new();

    // Common property changes across files
    public List<GlobalPropertyChangeInfo> CommonPropertyChanges { get; set; } = new();

    // Common category statistics across files
    public Dictionary<DifferenceCategory, int> TotalByCategory { get; set; } = new();

    // Files grouped by similarity
    public List<SimilarFileGroup> SimilarFileGroups { get; set; } = new();
}
