namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPropertyChangeInfo
{
    public string PropertyName { get; set; }

    // Changed to field for use with Interlocked.Increment
    public int _occurrenceCount;

    // Property wrapper for the field
    public int OccurrenceCount
    {
        get => _occurrenceCount;
        set => _occurrenceCount = value;
    }

    public Dictionary<string, string> CommonChanges { get; set; } = new();
    public List<string> AffectedFiles { get; set; } = new();
}