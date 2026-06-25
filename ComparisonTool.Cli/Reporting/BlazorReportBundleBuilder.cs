using System.Text.Json;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization.BlazorReport;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Builds the JSON payload for a Blazor WASM report.
/// Serializes the full <see cref="MultiFolderComparisonResult"/> and analysis data
/// into a <see cref="ReportBootstrapData"/> JSON that the Blazor report reads at runtime.
/// </summary>
internal static class BlazorReportBundleBuilder
{
    private const string BundledRawContentDirectoryName = "raw";

    /// <summary>
    /// Builds the bootstrap data object for the Blazor report from the given report context.
    /// </summary>
    /// <param name="context">The report context containing the comparison result and report metadata.</param>
    /// <param name="enhancedAnalysis">The optional enhanced structural analysis to embed in the report payload.</param>
    /// <returns>The report bootstrap data.</returns>
    public static async Task<ReportBootstrapData> BuildBootstrapDataAsync(ReportContext context, EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? enhancedAnalysis = null)
    {
        var reportResult = await CreateReportResultAsync(context.Result).ConfigureAwait(false);

        return new ReportBootstrapData
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
    }

    /// <summary>
    /// Builds a serialized JSON string for the Blazor report from the given report context.
    /// </summary>
    /// <param name="context">The report context containing the comparison result and report metadata.</param>
    /// <param name="enhancedAnalysis">The optional enhanced structural analysis to embed in the report payload.</param>
    /// <returns>The serialized report bootstrap JSON.</returns>
    public static async Task<string> BuildJsonAsync(ReportContext context, EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult? enhancedAnalysis = null)
    {
        var bootstrapData = await BuildBootstrapDataAsync(context, enhancedAnalysis).ConfigureAwait(false);
        return JsonSerializer.Serialize(bootstrapData, BlazorReportSerializerOptions.Default);
    }

    public static string BuildBundledRawContentPath(FilePairComparisonResult pair, int index) =>
        $"{BundledRawContentDirectoryName}/{ComparisonReportIdentity.BuildPairId(pair, index)}.json";

    public static string BuildFocusedBundledRawContentPath(FilePairComparisonResult pair, int index) =>
        $"{BundledRawContentDirectoryName}/focused-{ComparisonReportIdentity.BuildPairId(pair, index)}.json";

    public static bool ShouldWriteBundledRawContentSidecar(FilePairComparisonResult pair) =>
        !pair.HasError
        && !string.IsNullOrWhiteSpace(pair.File1Path)
        && !string.IsNullOrWhiteSpace(pair.File2Path);

    public static bool ShouldWriteFocusedBundledRawContentSidecar(FilePairComparisonResult pair) =>
        pair.HasFocusedRawContent
        && !string.IsNullOrWhiteSpace(pair.FocusedFile1Path)
        && !string.IsNullOrWhiteSpace(pair.FocusedFile2Path);

    public static Task<BundledRawContentData> BuildBundledRawContentDataAsync(FilePairComparisonResult pair) =>
        BuildBundledRawContentDataAsync(pair, RawContentVariant.Full);

    public static async Task<BundledRawContentData> BuildBundledRawContentDataAsync(FilePairComparisonResult pair, RawContentVariant variant)
    {
        var rawContentService = new RawContentService(NullLogger<RawContentService>.Instance);
        var rawContent = await rawContentService.LoadRawContentAsync(pair, variant).ConfigureAwait(false);

        if (!rawContent.IsLoaded)
        {
            return new BundledRawContentData
            {
                ErrorMessage = rawContent.ErrorMessage,
            };
        }

        return new BundledRawContentData
        {
            ContentA = StructuredTextDisplayFormatter.FormatForDisplay(
                rawContent.ContentA,
                pair.ContentTypeA,
                pair.File1Name),
            ContentB = StructuredTextDisplayFormatter.FormatForDisplay(
                rawContent.ContentB,
                pair.ContentTypeB,
                pair.File2Name),
            IsTruncatedA = rawContent.IsTruncatedA,
            IsTruncatedB = rawContent.IsTruncatedB,
        };
    }

    private static async Task<MultiFolderComparisonResult> CreateReportResultAsync(MultiFolderComparisonResult source)
    {
        var filePairResults = new List<FilePairComparisonResult>(source.FilePairResults.Count);

        for (var index = 0; index < source.FilePairResults.Count; index++)
        {
            var pair = source.FilePairResults[index];
            var clonedPair = ClonePair(pair);

            if (ShouldEmbedRawContent(pair))
            {
                await PopulateEmbeddedRawContentAsync(clonedPair, pair).ConfigureAwait(false);
            }
            else if (ShouldWriteBundledRawContentSidecar(pair))
            {
                clonedPair.BundledRawContentPath = BuildBundledRawContentPath(pair, index);
            }

            if (ShouldWriteFocusedBundledRawContentSidecar(pair))
            {
                clonedPair.FocusedBundledRawContentPath = BuildFocusedBundledRawContentPath(pair, index);
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

    private static bool ShouldEmbedRawContent(FilePairComparisonResult pair) =>
        pair.HasError
        && !string.IsNullOrWhiteSpace(pair.File1Path)
        && !string.IsNullOrWhiteSpace(pair.File2Path);

    private static async Task PopulateEmbeddedRawContentAsync(FilePairComparisonResult targetPair, FilePairComparisonResult sourcePair)
    {
        var bundledContent = await BuildBundledRawContentDataAsync(sourcePair).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(bundledContent.ErrorMessage))
        {
            return;
        }

        targetPair.HasEmbeddedRawContent = true;
        targetPair.EmbeddedRawContentA = bundledContent.ContentA;
        targetPair.EmbeddedRawContentB = bundledContent.ContentB;
        targetPair.EmbeddedRawContentTruncatedA = bundledContent.IsTruncatedA;
        targetPair.EmbeddedRawContentTruncatedB = bundledContent.IsTruncatedB;
    }

    private static FilePairComparisonResult ClonePair(FilePairComparisonResult pair) =>
        new FilePairComparisonResult
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
            BundledRawContentPath = pair.BundledRawContentPath,
            FocusedFile1Path = pair.FocusedFile1Path,
            FocusedFile2Path = pair.FocusedFile2Path,
            FocusedBundledRawContentPath = pair.FocusedBundledRawContentPath,
            FocusedRawContentRuleCount = pair.FocusedRawContentRuleCount,
            ErrorMessage = pair.ErrorMessage,
            ErrorType = pair.ErrorType,
        };
}