using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core;

public class GlobalPatternInfo
{
    public string PatternPath { get; set; }
    public int OccurrenceCount { get; set; }
    public int FileCount { get; set; }
    public List<string> AffectedFiles { get; set; } = new List<string>();
    public List<Difference> Examples { get; set; } = new List<Difference>();
}