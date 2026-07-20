namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Represents one field/path that appears frequently in structured differences.
/// </summary>
public sealed class MostAffectedField
{
    /// <summary>
    /// Gets or sets the normalized field/property path.
    /// </summary>
    public string FieldPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of distinct file pairs where this field had at least one difference.
    /// </summary>
    public int AffectedPairCount { get; set; }

    /// <summary>
    /// Gets or sets the total number of difference occurrences for this field across all pairs.
    /// </summary>
    public int OccurrenceCount { get; set; }
}

/// <summary>
/// Summary of common field-level differences for CLI reports.
/// </summary>
public sealed class MostAffectedFieldsSummary
{
    /// <summary>
    /// Gets an empty summary instance.
    /// </summary>
    public static MostAffectedFieldsSummary Empty { get; } = new ();

    /// <summary>
    /// Gets or sets all aggregated fields sorted by impact.
    /// </summary>
    public IReadOnlyList<MostAffectedField> Fields { get; set; } = Array.Empty<MostAffectedField>();

    /// <summary>
    /// Gets or sets the number of non-error pairs that had only raw-text differences
    /// and were excluded from field ranking.
    /// </summary>
    public int ExcludedRawTextPairCount { get; set; }

    /// <summary>
    /// Gets or sets the number of non-error pairs that contributed structured field differences.
    /// </summary>
    public int StructuredPairCount { get; set; }
}