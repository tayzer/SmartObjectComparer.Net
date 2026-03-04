namespace ComparisonTool.Core.Comparison.Presentation;

/// <summary>
/// Aggregated projection used by UI and report renderers.
/// </summary>
public sealed class ComparisonResultProjection
{
    /// <summary>Gets or sets all projected rows.</summary>
    public IReadOnlyList<ComparisonResultGridItem> Items { get; set; } = Array.Empty<ComparisonResultGridItem>();

    /// <summary>Gets or sets available category groups.</summary>
    public IReadOnlyList<string> AvailableGroups { get; set; } = Array.Empty<string>();

    /// <summary>Gets or sets the count of equal rows.</summary>
    public int EqualCount { get; set; }

    /// <summary>Gets or sets the count of different rows.</summary>
    public int DifferentCount { get; set; }

    /// <summary>Gets or sets the count of error rows.</summary>
    public int ErrorCount { get; set; }

    /// <summary>Gets or sets the count of status mismatch rows.</summary>
    public int StatusCodeMismatchCount { get; set; }

    /// <summary>Gets or sets the count of rows where both endpoints returned non-success.</summary>
    public int BothNonSuccessCount { get; set; }

    /// <summary>Gets or sets a value indicating whether any row has request outcome data.</summary>
    public bool HasHttpStatusData { get; set; }
}
