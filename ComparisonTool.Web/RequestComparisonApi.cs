using System.Buffers;
using System.Collections.Concurrent;
using System.Text.Json;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.AspNetCore.Mvc;

namespace ComparisonTool.Web;

/// <summary>
/// API endpoints for request-based comparison.
/// </summary>
public static class RequestComparisonApi
{
    private const int BufferSize = 81920; // 80KB buffer
    private static readonly ArrayPool<byte> BufferPool = ArrayPool<byte>.Shared;
    private static readonly ConcurrentDictionary<string, CancellationTokenSource> JobCancellationTokens = new();
    private static readonly SemaphoreSlim CacheLock = new(1, 1);

    public static void MapRequestComparisonApi(this WebApplication app)
    {
        var api = app.MapGroup("/api/requests")
            .WithTags("Request Comparison");

        // Upload request batch
        api.MapPost("/batch", UploadRequestBatch)
            .DisableAntiforgery()
            .WithName("UploadRequestBatch")
            .WithDescription("Upload a batch of request files for comparison");

        // Create comparison job
        api.MapPost("/compare", CreateComparisonJob)
            .WithName("CreateRequestComparisonJob")
            .WithDescription("Create a new request comparison job");

        // Get job status
        api.MapGet("/compare/{jobId}/status", GetJobStatus)
            .WithName("GetRequestComparisonJobStatus")
            .WithDescription("Get the status of a request comparison job");

        // Get job result
        api.MapGet("/compare/{jobId}/result", GetJobResult)
            .WithName("GetRequestComparisonJobResult")
            .WithDescription("Get the result of a completed request comparison job");

        // Cancel job
        api.MapPost("/compare/{jobId}/cancel", CancelJob)
            .WithName("CancelRequestComparisonJob")
            .WithDescription("Cancel a running request comparison job");

        // Get batch file list
        api.MapGet("/batch/{batchId}", GetBatchFiles)
            .WithName("GetRequestBatchFiles")
            .WithDescription("Get the list of files in a request batch");

        // Get batch by cache key
        api.MapGet("/batch/cache/{cacheKey}", GetBatchByCacheKey)
            .WithName("GetRequestBatchByCacheKey")
            .WithDescription("Get a cached request batch by cache key");
    }

    private static async Task<IResult> UploadRequestBatch(HttpRequest request)
    {
        if (!request.HasFormContentType)
        {
            return Results.BadRequest("Content-Type must be multipart/form-data");
        }

        var form = await request.ReadFormAsync().ConfigureAwait(false);
        var files = form.Files;
        var cacheKey = form["cacheKey"].FirstOrDefault();
        var uploadedFiles = new ConcurrentBag<string>();
        var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests");

        if (!Directory.Exists(tempPath))
        {
            Directory.CreateDirectory(tempPath);
        }

        var batchId = Guid.NewGuid().ToString("N")[..8];
        var batchPath = Path.Combine(tempPath, batchId);
        Directory.CreateDirectory(batchPath);

        // Pre-create directories
        var directories = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in files)
        {
            var filePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar)
                .Replace('/', Path.DirectorySeparatorChar);
            var destPath = Path.Combine(batchPath, filePath);
            var destDir = Path.GetDirectoryName(destPath) ?? batchPath;
            directories.Add(destDir);
        }

        foreach (var dir in directories)
        {
            Directory.CreateDirectory(dir);
        }

        // Process files in parallel
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount * 2, 16)
        };

        await Parallel.ForEachAsync(files, parallelOptions, async (file, ct) =>
        {
            try
            {
                var filePath = file.FileName.Replace('\\', Path.DirectorySeparatorChar)
                    .Replace('/', Path.DirectorySeparatorChar);
                var destPath = Path.Combine(batchPath, filePath);

                var buffer = BufferPool.Rent(BufferSize);
                try
                {
                    await using var sourceStream = file.OpenReadStream();
                    await using var destStream = new FileStream(
                        destPath,
                        FileMode.Create,
                        FileAccess.Write,
                        FileShare.None,
                        BufferSize,
                        FileOptions.Asynchronous | FileOptions.SequentialScan);

                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer.AsMemory(0, BufferSize), ct)) > 0)
                    {
                        await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                    }
                }
                finally
                {
                    BufferPool.Return(buffer);
                }

                uploadedFiles.Add(destPath);
            }
            catch
            {
                // Log and continue
            }
        }).ConfigureAwait(false);

        var sortedFiles = uploadedFiles.OrderBy(f => f, StringComparer.Ordinal).ToList();

        if (!string.IsNullOrWhiteSpace(cacheKey))
        {
            await UpdateCacheIndexAsync(tempPath, cacheKey, batchId, sortedFiles.Count)
                .ConfigureAwait(false);
        }

        if (sortedFiles.Count > 100)
        {
            var fileListPath = Path.Combine(batchPath, "_filelist.json");
            await File.WriteAllTextAsync(fileListPath, JsonSerializer.Serialize(sortedFiles))
                .ConfigureAwait(false);

            return Results.Ok(new RequestBatchUploadResponse
            {
                Uploaded = sortedFiles.Count,
                BatchId = batchId,
                FileListPath = fileListPath,
                CacheKey = cacheKey,
                CacheHit = false
            });
        }

        return Results.Ok(new RequestBatchUploadResponse
        {
            Uploaded = sortedFiles.Count,
            BatchId = batchId,
            Files = sortedFiles,
            CacheKey = cacheKey,
            CacheHit = false
        });
    }

    private static async Task<IResult> CreateComparisonJob(
        [FromBody] CreateRequestComparisonJobRequest request,
        [FromServices] RequestComparisonJobService jobService,
        [FromServices] IComparisonProgressPublisher progressPublisher,
        [FromServices] ILoggerFactory loggerFactory)
    {
        // Validate request
        if (string.IsNullOrEmpty(request.RequestBatchId))
        {
            return Results.BadRequest("RequestBatchId is required");
        }

        if (!Uri.TryCreate(request.EndpointA, UriKind.Absolute, out _))
        {
            return Results.BadRequest("EndpointA must be a valid URL");
        }

        if (!Uri.TryCreate(request.EndpointB, UriKind.Absolute, out _))
        {
            return Results.BadRequest("EndpointB must be a valid URL");
        }

        var job = jobService.CreateJob(request);

        // Publish initial progress event (best-effort, don't block job creation)
        try
        {
            await progressPublisher.PublishAsync(new ComparisonProgressUpdate
            {
                JobId = job.JobId,
                Phase = ComparisonPhase.Initializing,
                PercentComplete = 0,
                Message = "Job created, initializing...",
                Timestamp = DateTimeOffset.UtcNow
            });
        }
        catch (Exception ex)
        {
            var logger = loggerFactory.CreateLogger("RequestComparisonApi");
            logger.LogWarning(ex, "Failed to publish initial progress for job {JobId}", job.JobId);
        }

        // Start job execution in background
        var cts = new CancellationTokenSource();
        JobCancellationTokens[job.JobId] = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await jobService.ExecuteJobAsync(job.JobId, null, cts.Token);
            }
            finally
            {
                JobCancellationTokens.TryRemove(job.JobId, out _);
            }
        });

        return Results.Ok(new CreateJobResponse { JobId = job.JobId });
    }

    private static IResult GetJobStatus(
        string jobId,
        [FromServices] RequestComparisonJobService jobService)
    {
        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            return Results.NotFound($"Job {jobId} not found");
        }

        return Results.Ok(new JobStatusResponse
        {
            Status = job.Status.ToString(),
            Completed = job.CompletedRequests,
            Total = job.TotalRequests,
            Message = job.StatusMessage,
            Error = job.ErrorMessage
        });
    }

    private static IResult GetJobResult(
        string jobId,
        [FromServices] RequestComparisonJobService jobService)
    {
        var job = jobService.GetJob(jobId);
        if (job == null)
        {
            return Results.NotFound($"Job {jobId} not found");
        }

        if (job.Status != RequestComparisonStatus.Completed)
        {
            return Results.BadRequest($"Job is not completed. Current status: {job.Status}");
        }

        var result = jobService.GetResult(jobId);
        if (result == null)
        {
            return Results.NotFound("Result not available");
        }

        return Results.Ok(result);
    }

    private static IResult CancelJob(string jobId)
    {
        if (JobCancellationTokens.TryGetValue(jobId, out var cts))
        {
            cts.Cancel();
            return Results.Ok(new { message = "Cancellation requested" });
        }

        return Results.NotFound($"Job {jobId} not found or already completed");
    }

    private static IResult GetBatchFiles(string batchId)
    {
        var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests");
        var batchPath = Path.Combine(tempPath, batchId);
        var fileListPath = Path.Combine(batchPath, "_filelist.json");

        if (!Directory.Exists(batchPath))
        {
            return Results.NotFound($"Batch {batchId} not found");
        }

        if (File.Exists(fileListPath))
        {
            var fileList = JsonSerializer.Deserialize<List<string>>(
                File.ReadAllText(fileListPath)) ?? new List<string>();
            return Results.Ok(new { files = fileList });
        }

        // List files directly
        var files = Directory.GetFiles(batchPath, "*", SearchOption.AllDirectories)
            .Where(f => !Path.GetFileName(f).StartsWith("_"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        return Results.Ok(new { files });
    }

    private static async Task<IResult> GetBatchByCacheKey(string cacheKey)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            return Results.BadRequest("cacheKey is required");
        }

        var tempPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests");
        var indexPath = GetCacheIndexPath(tempPath);

        var cache = await ReadCacheIndexAsync(indexPath).ConfigureAwait(false);
        if (!cache.TryGetValue(cacheKey, out var entry))
        {
            return Results.NotFound("Cache key not found");
        }

        var batchPath = Path.Combine(tempPath, entry.BatchId);
        if (!Directory.Exists(batchPath))
        {
            await RemoveCacheEntryAsync(indexPath, cacheKey).ConfigureAwait(false);
            return Results.NotFound("Cached batch not found");
        }

        return Results.Ok(new RequestBatchCacheLookupResponse
        {
            Found = true,
            CacheKey = cacheKey,
            BatchId = entry.BatchId,
            Uploaded = entry.Uploaded
        });
    }

    private static string GetCacheIndexPath(string tempPath) =>
        Path.Combine(tempPath, "_cache_index.json");

    private static async Task UpdateCacheIndexAsync(
        string tempPath,
        string cacheKey,
        string batchId,
        int uploadedCount)
    {
        var indexPath = GetCacheIndexPath(tempPath);
        await CacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cache = await ReadCacheIndexUnsafeAsync(indexPath).ConfigureAwait(false);
            cache[cacheKey] = new RequestBatchCacheEntry
            {
                BatchId = batchId,
                Uploaded = uploadedCount,
                UpdatedUtc = DateTimeOffset.UtcNow
            };

            await WriteCacheIndexUnsafeAsync(indexPath, cache).ConfigureAwait(false);
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private static async Task<Dictionary<string, RequestBatchCacheEntry>> ReadCacheIndexAsync(string indexPath)
    {
        await CacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            return await ReadCacheIndexUnsafeAsync(indexPath).ConfigureAwait(false);
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private static async Task<Dictionary<string, RequestBatchCacheEntry>> ReadCacheIndexUnsafeAsync(string indexPath)
    {
        if (!File.Exists(indexPath))
        {
            return new Dictionary<string, RequestBatchCacheEntry>(StringComparer.Ordinal);
        }

        try
        {
            var json = await File.ReadAllTextAsync(indexPath).ConfigureAwait(false);
            return JsonSerializer.Deserialize<Dictionary<string, RequestBatchCacheEntry>>(json)
                   ?? new Dictionary<string, RequestBatchCacheEntry>(StringComparer.Ordinal);
        }
        catch
        {
            return new Dictionary<string, RequestBatchCacheEntry>(StringComparer.Ordinal);
        }
    }

    private static async Task WriteCacheIndexUnsafeAsync(
        string indexPath,
        Dictionary<string, RequestBatchCacheEntry> cache)
    {
        var json = JsonSerializer.Serialize(cache);
        await File.WriteAllTextAsync(indexPath, json).ConfigureAwait(false);
    }

    private static async Task RemoveCacheEntryAsync(string indexPath, string cacheKey)
    {
        await CacheLock.WaitAsync().ConfigureAwait(false);
        try
        {
            var cache = await ReadCacheIndexUnsafeAsync(indexPath).ConfigureAwait(false);
            if (cache.Remove(cacheKey))
            {
                await WriteCacheIndexUnsafeAsync(indexPath, cache).ConfigureAwait(false);
            }
        }
        finally
        {
            CacheLock.Release();
        }
    }

    private sealed record RequestBatchCacheEntry
    {
        public required string BatchId { get; init; }
        public int Uploaded { get; init; }
        public DateTimeOffset UpdatedUtc { get; init; }
    }
}
