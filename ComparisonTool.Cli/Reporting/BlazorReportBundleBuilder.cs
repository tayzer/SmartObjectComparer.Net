using System.Text.Json;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization.BlazorReport;
using Microsoft.Extensions.Logging.Abstractions;

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
    public static async Task<string> BuildJsonAsync(ReportContext context, EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? enhancedAnalysis = null)
    {
        var reportResult = await CreateReportResultAsync(context.Result).ConfigureAwait(false);

        var bootstrapData = new ReportBootstrapData
        {
            Result = reportResult,
            EnhancedAnalysis = enhancedAnalysis,
            SemanticAnalysis = reportResult.Metadata.TryGetValue("SemanticAnalysis", out var sa)
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

    private static async Task<MultiFolderComparisonResult> CreateReportResultAsync(MultiFolderComparisonResult source)
    {
        var rawContentService = new RawContentService(NullLogger<RawContentService>.Instance);
        var filePairResults = new List<FilePairComparisonResult>(source.FilePairResults.Count);

        foreach (var pair in source.FilePairResults)
        {
            var clonedPair = ClonePair(pair);

            if (ShouldEmbedRawContent(pair))
            {
                var rawContent = await rawContentService.LoadRawContentAsync(pair).ConfigureAwait(false);
                if (rawContent.IsLoaded)
                {
                    clonedPair.HasEmbeddedRawContent = true;
                    clonedPair.EmbeddedRawContentA = rawContent.ContentA;
                    clonedPair.EmbeddedRawContentB = rawContent.ContentB;
                    clonedPair.EmbeddedRawContentTruncatedA = rawContent.IsTruncatedA;
                    clonedPair.EmbeddedRawContentTruncatedB = rawContent.IsTruncatedB;
                }
            }

            filePairResults.Add(clonedPair);
        }

        return new MultiFolderComparisonResult
        {
            AllEqual = source.AllEqual,
            TotalPairsCompared = source.TotalPairsCompared,
            FilePairResults = filePairResults,
            Metadata = new Dictionary<string, object>(source.Metadata, StringComparer.Ordinal),
        };
    }

    private static bool ShouldEmbedRawContent(FilePairComparisonResult pair)
    {
        return !string.IsNullOrWhiteSpace(pair.File1Path)
            && !string.IsNullOrWhiteSpace(pair.File2Path);
    }

    private static FilePairComparisonResult ClonePair(FilePairComparisonResult pair)
    {
        return new FilePairComparisonResult
        {
            File1Name = pair.File1Name,
            File2Name = pair.File2Name,
            File1Path = pair.File1Path,
            File2Path = pair.File2Path,
            RequestRelativePath = pair.RequestRelativePath,
            Result = pair.Result,
            Summary = pair.Summary,
            HttpStatusCodeA = pair.HttpStatusCodeA,
            HttpStatusCodeB = pair.HttpStatusCodeB,
            ContentTypeA = pair.ContentTypeA,
            ContentTypeB = pair.ContentTypeB,
            PairOutcome = pair.PairOutcome,
            RawTextDifferences = pair.RawTextDifferences,
            ErrorMessage = pair.ErrorMessage,
            ErrorType = pair.ErrorType,
        };
    }
}
