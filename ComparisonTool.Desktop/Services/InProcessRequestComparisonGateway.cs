using System.Collections.Concurrent;
using System.IO;
using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// In-process request comparison gateway. Replaces HTTP API calls with direct
/// service invocations. Files are staged directly on disk — no multipart upload needed.
/// </summary>
public class InProcessRequestComparisonGateway : IRequestComparisonGateway
{
    private readonly RequestComparisonJobService _jobService;
    private readonly ILogger<InProcessRequestComparisonGateway> _logger;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new();

    public InProcessRequestComparisonGateway(
        RequestComparisonJobService jobService,
        ILogger<InProcessRequestComparisonGateway> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<RequestBatchResult> StageRequestFilesAsync(
        IReadOnlyList<string> filePaths,
        string? cacheKey = null)
    {
        // In desktop mode, files are already on disk — no upload needed.
        // Create a batch folder and copy/link files into it for the parser.
        var batchId = Guid.NewGuid().ToString("N")[..8];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);

        var copiedCount = 0;
        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Skipping non-existent file: {Path}", filePath);
                continue;
            }

            var relativePath = Path.GetFileName(filePath);
            var destPath = Path.Combine(batchPath, relativePath);
            File.Copy(filePath, destPath, overwrite: true);
            copiedCount++;
        }

        _logger.LogInformation("Staged {Count} request files in batch {BatchId}", copiedCount, batchId);

        return Task.FromResult(new RequestBatchResult(batchId, copiedCount, CacheHit: false));
    }

    /// <inheritdoc/>
    public async Task<string> StartComparisonAsync(
        CreateRequestComparisonJobRequest request,
        CancellationToken cancellationToken = default)
    {
        var job = _jobService.CreateJob(request);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _jobCancellationTokens[job.JobId] = cts;

        // Fire-and-forget execution (same pattern as Web host)
        _ = Task.Run(async () =>
        {
            try
            {
                await _jobService.ExecuteJobAsync(job.JobId, null, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Job {JobId} was cancelled", job.JobId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Job {JobId} failed during execution", job.JobId);
            }
            finally
            {
                _jobCancellationTokens.TryRemove(job.JobId, out _);
            }
        });

        return job.JobId;
    }

    /// <inheritdoc/>
    public Task<RequestJobStatus> GetJobStatusAsync(string jobId)
    {
        var job = _jobService.GetJob(jobId);
        if (job == null)
        {
            return Task.FromResult(new RequestJobStatus("NotFound", 0, 0, null, $"Job {jobId} not found"));
        }

        return Task.FromResult(new RequestJobStatus(
            job.Status.ToString(),
            job.CompletedRequests,
            job.TotalRequests,
            job.StatusMessage,
            job.ErrorMessage));
    }

    /// <inheritdoc/>
    public Task<MultiFolderComparisonResult?> GetJobResultAsync(string jobId)
    {
        return Task.FromResult(_jobService.GetResult(jobId));
    }

    /// <inheritdoc/>
    public Task CancelJobAsync(string jobId)
    {
        if (_jobCancellationTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            _logger.LogInformation("Cancellation requested for job {JobId}", jobId);
        }

        return Task.CompletedTask;
    }
}
