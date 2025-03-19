using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core;

public class MultiFolderComparisonResult
{
    public bool AllEqual { get; set; } = true;
    public int TotalPairsCompared { get; set; }
    public List<FilePairComparisonResult> FilePairResults { get; set; } = new List<FilePairComparisonResult>();
}

public class ComparisonPatternAnalysis
{
    public int TotalFilesPaired { get; set; }
    public int FilesWithDifferences { get; set; }
    public int TotalDifferences { get; set; }

    // Common path patterns across files
    public List<GlobalPatternInfo> CommonPathPatterns { get; set; } = new List<GlobalPatternInfo>();

    // Common property changes across files
    public List<GlobalPropertyChangeInfo> CommonPropertyChanges { get; set; } = new List<GlobalPropertyChangeInfo>();

    // Common category statistics across files
    public Dictionary<DifferenceCategory, int> TotalByCategory { get; set; } = new Dictionary<DifferenceCategory, int>();

    // Files grouped by similarity
    public List<SimilarFileGroup> SimilarFileGroups { get; set; } = new List<SimilarFileGroup>();
}

public class GlobalPatternInfo
{
    public string PatternPath { get; set; }
    public int OccurrenceCount { get; set; }
    public int FileCount { get; set; }
    public List<string> AffectedFiles { get; set; } = new List<string>();
    public List<Difference> Examples { get; set; } = new List<Difference>();
}

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; }
    public int OccurrenceCount { get; set; }
    public Dictionary<string, string> CommonChanges { get; set; } = new Dictionary<string, string>();
    public List<string> AffectedFiles { get; set; } = new List<string>();
}

public class SimilarFileGroup
{
    public string GroupName { get; set; }
    public int FileCount { get; set; }
    public List<string> FilePairs { get; set; } = new List<string>();
    public string CommonPattern { get; set; }
}