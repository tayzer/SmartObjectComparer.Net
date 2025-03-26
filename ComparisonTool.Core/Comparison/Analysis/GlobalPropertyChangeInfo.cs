namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; }
    public int OccurrenceCount { get; set; }
    public Dictionary<string, string> CommonChanges { get; set; } = new();
    public List<string> AffectedFiles { get; set; } = new();
}