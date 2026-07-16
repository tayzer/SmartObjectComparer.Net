namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Request-comparison timing metadata stored on comparison results for reports and diagnostics.
/// </summary>
public sealed class RequestComparisonRunTimings
{
    public const string MetadataKey = "RequestComparisonRunTimings";

    public int TotalRequests { get; init; }

    public int SuccessfulRequests { get; init; }

    public int TotalPairsCompared { get; init; }

    public bool LargeBatchMode { get; init; }

    public int LargeBatchTotalChunks { get; init; }

    public long ParsingMs { get; init; }

    public long RequestExecutionMs { get; init; }

    public long ResponseComparisonMs { get; init; }

    public long FocusedRawContentMs { get; init; }

    public long FinalizationMs { get; init; }

    public long TotalElapsedMs { get; init; }
}
