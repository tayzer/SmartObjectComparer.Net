using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPatternInfo
{
    public string PatternPath { get; set; }
    public int OccurrenceCount { get; set; }
    public int FileCount { get; set; }
    public List<string> AffectedFiles { get; set; } = new();
    public List<Difference> Examples { get; set; } = new();
}