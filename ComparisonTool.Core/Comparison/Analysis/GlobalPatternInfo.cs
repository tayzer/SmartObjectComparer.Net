using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Information about a pattern of differences that appears across multiple files
/// Modified for thread safety with a field instead of property for OccurrenceCount.
/// </summary>
public class GlobalPatternInfo
{
    // Private fields retained for use with Interlocked operations elsewhere in the codebase
    private int occurrenceCountValue;
    private int fileCountValue;

    public string PatternPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the occurrence count. Uses backing field for Interlocked operations.
    /// </summary>
    public int OccurrenceCount
    {
        get => occurrenceCountValue;
        set => occurrenceCountValue = value;
    }

    /// <summary>
    /// Gets the occurrence count value reference for Interlocked operations.
    /// </summary>
    public ref int OccurrenceCountRef => ref occurrenceCountValue;

    /// <summary>
    /// Gets or sets the file count. Uses backing field for Interlocked operations.
    /// </summary>
    public int FileCount
    {
        get => fileCountValue;
        set => fileCountValue = value;
    }

    /// <summary>
    /// Gets the file count value reference for Interlocked operations.
    /// </summary>
    public ref int FileCountRef => ref fileCountValue;

    public IList<string> AffectedFiles { get; set; } = new List<string>();

    public IList<Difference> Examples { get; set; } = new List<Difference>();
}
