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
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _jobCancellationTokens = new ConcurrentDictionary<string, CancellationTokenSource>();

    public InProcessRequestComparisonGateway(
        RequestComparisonJobService jobService,
        ILogger<InProcessRequestComparisonGateway> logger)
    {
        _jobService = jobService;
        _logger = logger;
    }

    public async Task<RequestBatchResult> StateRequestStreamsAsync(IEnumerable<(string FileName, Stream Content)> files, string? cacheKey = null)
    {
        var batchId = Guid.NewGuid().ToString("N")[..8];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);

        var copiedCount = 0;
        var stagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            var destPath = GetSafeUniqueDestinationPath(batchPath, file.FileName, stagedPaths);
            var destDir = Path.GetDirectoryName(destPath);
            if (destDir != null && Directory.Exists(destDir) == false)
            {
                Directory.CreateDirectory(destDir);
            }

            await using var fileStream = new FileStream(
                destPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await file.Content.CopyToAsync(fileStream);
            copiedCount++;
        }

        _logger.LogInformation("Staged {Count} request streams in batch {BatchId}", copiedCount, batchId);

        return new RequestBatchResult(batchId, copiedCount, CacheHit: false);
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
        var stagedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in filePaths)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("Skipping non-existent file: {Path}", filePath);
                continue;
            }

            var destPath = GetSafeUniqueDestinationPath(batchPath, Path.GetFileName(filePath), stagedPaths);
            File.Copy(filePath, destPath, overwrite: false);
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

    private static string GetSafeUniqueDestinationPath(string batchPath, string fileName, ISet<string> stagedPaths)
    {
        var relativePath = NormalizeRelativeRequestPath(fileName);
        var batchRoot = Path.GetFullPath(batchPath);
        var destinationPath = Path.GetFullPath(Path.Combine(batchRoot, relativePath));

        if (!IsPathInsideDirectory(destinationPath, batchRoot))
        {
            throw new InvalidOperationException($"Request file name '{fileName}' resolves outside the staging folder.");
        }

        return GetUniqueDestinationPath(destinationPath, stagedPaths);
    }

    private static string NormalizeRelativeRequestPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new InvalidOperationException("A selected request file did not include a file name.");
        }

        var normalized = fileName.Replace('\\', '/').Trim();
        if (Path.IsPathRooted(normalized) || !string.IsNullOrWhiteSpace(Path.GetPathRoot(normalized)))
        {
            normalized = Path.GetFileName(normalized);
        }

        var parts = normalized
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => part != ".")
            .ToArray();

        if (parts.Length == 0)
        {
            throw new InvalidOperationException("A selected request file did not include a valid file name.");
        }

        foreach (var part in parts)
        {
            if (part == "..")
            {
                throw new InvalidOperationException($"Request file name '{fileName}' contains an unsupported parent-directory segment.");
            }

            if (part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                throw new InvalidOperationException($"Request file name '{fileName}' contains unsupported characters.");
            }
        }

        return Path.Combine(parts);
    }

    private static bool IsPathInsideDirectory(string path, string directory)
    {
        var directoryRoot = directory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
        return path.StartsWith(directoryRoot, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetUniqueDestinationPath(string destinationPath, ISet<string> stagedPaths)
    {
        var candidate = destinationPath;
        var directory = Path.GetDirectoryName(destinationPath) ?? string.Empty;
        var nameWithoutExtension = Path.GetFileNameWithoutExtension(destinationPath);
        var extension = Path.GetExtension(destinationPath);
        var suffix = 2;

        while (stagedPaths.Contains(candidate) || File.Exists(candidate))
        {
            candidate = Path.Combine(directory, $"{nameWithoutExtension} ({suffix}){extension}");
            suffix++;
        }

        stagedPaths.Add(candidate);
        return candidate;
    }
}
