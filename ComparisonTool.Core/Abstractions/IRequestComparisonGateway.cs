using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic gateway for request comparison operations.
/// Web implementation calls HTTP APIs (/api/requests/*).
/// Desktop implementation invokes services directly in-process.
/// </summary>
public interface IRequestComparisonGateway
{
    /// <summary>
    /// Stages request files for comparison and returns a batch identifier.
    /// </summary>
    /// <param name="filePaths">Absolute paths to request files on disk.</param>
    /// <param name="cacheKey">Optional cache key for deduplication.</param>
    /// <returns>Upload result with batch ID and file count.</returns>
    Task<RequestBatchResult> StageRequestFilesAsync(
        IReadOnlyList<string> filePaths,
        string? cacheKey = null);

    /// <summary>
    /// Creates and starts a comparison job.
    /// </summary>
    /// <param name="request">Job creation request.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The job ID.</returns>
    Task<string> StartComparisonAsync(
        CreateRequestComparisonJobRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current status of a comparison job.
    /// </summary>
    Task<RequestJobStatus> GetJobStatusAsync(string jobId);

    /// <summary>
    /// Gets the result of a completed comparison job.
    /// </summary>
    Task<MultiFolderComparisonResult?> GetJobResultAsync(string jobId);

    /// <summary>
    /// Cancels a running comparison job.
    /// </summary>
    Task CancelJobAsync(string jobId);
}

/// <summary>
/// Result of staging request files for comparison.
/// </summary>
public record RequestBatchResult(
    string BatchId,
    int FileCount,
    bool CacheHit);

/// <summary>
/// Status of a request comparison job.
/// </summary>
public record RequestJobStatus(
    string Status,
    int Completed,
    int Total,
    string? Message,
    string? Error);
