using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Report.Services;

/// <summary>
/// No-op request comparison gateway — the report viewer doesn't execute live comparisons.
/// </summary>
public sealed class WasmRequestComparisonGateway : IRequestComparisonGateway
{
    public Task<RequestBatchResult> StageRequestFilesAsync(IReadOnlyList<string> filePaths, string? cacheKey = null)
        => Task.FromResult(new RequestBatchResult(string.Empty, 0, false));

    public Task<string> StartComparisonAsync(CreateRequestComparisonJobRequest request, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);

    public Task<RequestJobStatus> GetJobStatusAsync(string jobId)
        => Task.FromResult(new RequestJobStatus("NotSupported", 0, 0, "Report viewer does not support live comparisons.", null));

    public Task<MultiFolderComparisonResult?> GetJobResultAsync(string jobId)
        => Task.FromResult<MultiFolderComparisonResult?>(null);

    public Task CancelJobAsync(string jobId)
        => Task.CompletedTask;
}
