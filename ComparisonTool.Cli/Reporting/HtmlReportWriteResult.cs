namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Represents output paths produced by HTML report generation.
/// </summary>
public sealed class HtmlReportWriteResult
{
    /// <summary>
    /// Gets or sets the primary path to open (single HTML file or index page).
    /// </summary>
    public string PrimaryPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the number of generated per-pair detail pages.
    /// </summary>
    public int DetailPageCount { get; set; }
}
