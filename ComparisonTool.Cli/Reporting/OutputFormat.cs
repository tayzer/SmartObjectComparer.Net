namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Supported output formats for CLI reports.
/// </summary>
public enum OutputFormat
{
    /// <summary>Human-readable console summary.</summary>
    Console,

    /// <summary>Machine-readable JSON file.</summary>
    Json,

    /// <summary>Markdown report file.</summary>
    Markdown,

    /// <summary>Interactive HTML report output.</summary>
    Html,
}
