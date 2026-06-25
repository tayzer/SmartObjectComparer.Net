namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Configuration values used when request comparison runs a large desktop batch.
/// </summary>
public sealed class RequestComparisonLargeBatchOptions
{
    /// <summary>Number of request files at which large-batch behavior is enabled.</summary>
    public int LargeBatchThreshold { get; set; } = 1000;

    /// <summary>Number of request files processed per internal chunk.</summary>
    public int LargeBatchChunkSize { get; set; } = 500;

    /// <summary>Default concurrency applied by desktop UI when a large batch is selected.</summary>
    public int LargeBatchDefaultConcurrency { get; set; } = 32;
}
