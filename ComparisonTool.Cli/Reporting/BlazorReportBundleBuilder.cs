using System.Text.Json;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization.BlazorReport;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Builds the JSON payload for a Blazor WASM report.
/// Serializes the full <see cref="MultiFolderComparisonResult"/> and analysis data
/// into a <see cref="ReportBootstrapData"/> JSON that the Blazor report reads at runtime.
/// </summary>
internal static class BlazorReportBundleBuilder
{
    /// <summary>
    /// Builds a serialized JSON string for the Blazor report from the given report context.
    /// </summary>
    public static string BuildJson(ReportContext context, EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? enhancedAnalysis = null)
    {
        var bootstrapData = new ReportBootstrapData
        {
            Result = context.Result,
            EnhancedAnalysis = enhancedAnalysis,
            SemanticAnalysis = context.Result.Metadata.TryGetValue("SemanticAnalysis", out var sa)
                ? sa as SemanticDifferenceAnalysis
                : null,
            Metadata = new ReportMetadata
            {
                ReportId = ComparisonReportIdentity.BuildReportId(context),
                GeneratedAt = context.GeneratedAtUtc.ToString("O"),
                Command = context.CommandName,
                ModelName = context.ModelName,
                Directory1 = context.Directory1,
                Directory2 = context.Directory2,
                EndpointA = context.EndpointA,
                EndpointB = context.EndpointB,
                JobId = context.JobId,
                ElapsedSeconds = Math.Round(context.Elapsed.TotalSeconds, 2),
            },
        };

        return JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
    }
}
