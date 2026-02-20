using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Contextual data passed to report writers.
/// </summary>
public class ReportContext
{
    /// <summary>Gets or sets the comparison result.</summary>
    required public MultiFolderComparisonResult Result { get; set; }

    /// <summary>Gets or sets the elapsed wall-clock time.</summary>
    public TimeSpan Elapsed { get; set; }

    /// <summary>Gets or sets the command name that produced this result.</summary>
    public string CommandName { get; set; } = string.Empty;

    // -- Folder compare context --

    /// <summary>Gets or sets the first directory path (folder compare).</summary>
    public string? Directory1 { get; set; }

    /// <summary>Gets or sets the second directory path (folder compare).</summary>
    public string? Directory2 { get; set; }

    // -- Request compare context --

    /// <summary>Gets or sets endpoint A URL (request compare).</summary>
    public string? EndpointA { get; set; }

    /// <summary>Gets or sets endpoint B URL (request compare).</summary>
    public string? EndpointB { get; set; }

    /// <summary>Gets or sets the job ID (request compare).</summary>
    public string? JobId { get; set; }

    /// <summary>Gets or sets the model name used for comparison.</summary>
    public string? ModelName { get; set; }

    /// <summary>
    /// Gets or sets the computed summary of most affected fields across structured differences.
    /// </summary>
    public MostAffectedFieldsSummary MostAffectedFields { get; set; } = MostAffectedFieldsSummary.Empty;

    /// <summary>
    /// Gets or sets the maximum number of file pairs per markdown page.
    /// When greater than zero, overflow pairs are written to detail pages in a subdirectory.
    /// When zero, all pairs are written to a single file with no pagination.
    /// Defaults to <c>50</c>.
    /// </summary>
    public int MarkdownPageSize { get; set; } = 50;

    /// <summary>
    /// Gets or sets the HTML export mode used by <see cref="HtmlReportWriter"/>.
    /// </summary>
    public HtmlReportMode HtmlMode { get; set; } = HtmlReportMode.SingleFile;
}
