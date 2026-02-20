namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Controls how CLI HTML reports are emitted.
/// </summary>
public enum HtmlReportMode
{
    /// <summary>
    /// Writes a single self-contained HTML document.
    /// </summary>
    SingleFile,

    /// <summary>
    /// Writes a static-site folder with an index page and per-pair detail pages.
    /// </summary>
    StaticSite,
}
