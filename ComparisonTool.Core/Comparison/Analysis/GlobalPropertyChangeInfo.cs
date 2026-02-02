// <copyright file="GlobalPropertyChangeInfo.cs" company="PlaceholderCompany">
namespace ComparisonTool.Core.Comparison.Analysis;

public class GlobalPropertyChangeInfo
{
    // Private field retained for use with Interlocked operations
    private int occurrenceCountValue;

    public string PropertyName { get; set; } = string.Empty;

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

    public IDictionary<string, string> CommonChanges { get; set; } = new Dictionary<string, string>(StringComparer.Ordinal);

    public IList<string> AffectedFiles { get; set; } = new List<string>();
}
