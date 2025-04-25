using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Information about a pattern of differences that appears across multiple files
/// Modified for thread safety with a field instead of property for OccurrenceCount
/// </summary>
public class GlobalPatternInfo
{
    public string PatternPath { get; set; }

    public int _occurrenceCount;

    // Property wrapper for the field
    public int OccurrenceCount
    {
        get => _occurrenceCount;
        set => _occurrenceCount = value;
    }

    public int _fileCount;

    // Property wrapper for the field
    public int FileCount
    {
        get => _fileCount;
        set => _fileCount = value;
    }

    public List<string> AffectedFiles { get; set; } = new();
    public List<Difference> Examples { get; set; } = new();
}