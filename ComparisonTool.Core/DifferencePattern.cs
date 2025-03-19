using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core;

/// <summary>
/// Represents a pattern of differences
/// </summary>
public class DifferencePattern
{
    public string Pattern { get; set; }
    public string PropertyPath { get; set; }
    public int OccurrenceCount { get; set; }
    public List<Difference> Examples { get; set; } = new List<Difference>();
}