using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Writes a machine-readable JSON report to a file.
/// </summary>
public static class JsonReportWriter
{
    private static readonly JsonSerializerOptions SerializerOptions = new ()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Serializes the comparison result to a JSON file.
    /// </summary>
    public static async Task WriteAsync(ReportContext context, string outputPath)
    {
        var result = context.Result;
        var pairs = result.FilePairResults;

        var report = new
        {
            generatedAt = DateTime.UtcNow,
            command = context.CommandName,
            model = context.ModelName,
            directory1 = context.Directory1,
            directory2 = context.Directory2,
            endpointA = context.EndpointA,
            endpointB = context.EndpointB,
            jobId = context.JobId,
            elapsedSeconds = Math.Round(context.Elapsed.TotalSeconds, 2),
            summary = new
            {
                totalPairs = result.TotalPairsCompared,
                allEqual = result.AllEqual,
                equalCount = pairs.Count(p => p.AreEqual),
                differentCount = pairs.Count(p => !p.AreEqual && !p.HasError),
                errorCount = pairs.Count(p => p.HasError),
            },
            filePairs = pairs.Select(p => new
            {
                file1 = p.File1Name,
                file2 = p.File2Name,
                areEqual = p.AreEqual,
                hasError = p.HasError,
                errorMessage = p.ErrorMessage,
                pairOutcome = p.PairOutcome?.ToString(),
                httpStatusA = p.HttpStatusCodeA,
                httpStatusB = p.HttpStatusCodeB,
                differenceCount = p.Result?.Differences?.Count ?? p.RawTextDifferences?.Count ?? 0,
                differences = p.Result?.Differences?.Select(d => new
                {
                    propertyName = d.PropertyName,
                    expected = d.Object1Value,
                    actual = d.Object2Value,
                }).ToList(),
                rawTextDifferences = p.RawTextDifferences?.Select(r => new
                {
                    type = r.Type.ToString(),
                    lineNumberA = r.LineNumberA,
                    lineNumberB = r.LineNumberB,
                    textA = r.TextA,
                    textB = r.TextB,
                    description = r.Description,
                }).ToList(),
            }).ToList(),
            metadata = result.Metadata,
        };

        var json = JsonSerializer.Serialize(report, SerializerOptions);
        await File.WriteAllTextAsync(outputPath, json);
    }
}
