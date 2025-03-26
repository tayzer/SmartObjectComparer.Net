namespace ComparisonTool.Core;

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; }
    public int OccurrenceCount { get; set; }
    public Dictionary<string, string> CommonChanges { get; set; } = new Dictionary<string, string>();
    public List<string> AffectedFiles { get; set; } = new List<string>();
}