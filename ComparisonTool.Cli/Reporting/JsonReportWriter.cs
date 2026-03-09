using System.Text.Json;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a machine-readable JSON report to a file.
/// </summary>
public static class JsonReportWriter
{
    /// <summary>
    /// Serializes the comparison result to a JSON file.
    /// </summary>
    public static async Task WriteAsync(ReportContext context, string outputPath)
    {
        var report = ComparisonReportMapper.Map(context);
        var json = JsonSerializer.Serialize(report, ComparisonReportJson.IndentedOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }
}
