using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http.Headers;
using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for executing HTTP requests against two endpoints with bounded concurrency.
/// </summary>
public class RequestExecutionService : IDisposable
{
    private readonly ILogger<RequestExecutionService> _logger;
    private readonly HttpClient _httpClient;
    private readonly ResponseMaskingService _responseMaskingService;
    private readonly RequestComparisonAlternateContractTransformationService _alternateContractTransformationService;
    private const int BufferSize = 81920; // 80KB buffer

    public RequestExecutionService(
        ILogger<RequestExecutionService> logger,
        IHttpClientFactory httpClientFactory,
        ResponseMaskingService responseMaskingService,
        RequestComparisonAlternateContractTransformationService alternateContractTransformationService)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("RequestComparison");
        _responseMaskingService = responseMaskingService;
        _alternateContractTransformationService = alternateContractTransformationService;
    }

    /// <summary>
    /// Executes all requests against both endpoints with bounded concurrency.
    /// </summary>
    public async Task<IReadOnlyList<RequestExecutionResult>> ExecuteRequestsAsync(
        RequestComparisonJob job,
        IReadOnlyList<RequestFileInfo> requests,
        IProgress<(int Completed, int Total, string Message)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new ConcurrentBag<RequestExecutionResult>();
        var completedCount = 0;

        // Create response directories
        var jobPath = Path.Combine(Path.GetTempPath(), "ComparisonToolJobs", job.JobId);
        job.ResponsePathA = Path.Combine(jobPath, "endpointA");
        job.ResponsePathB = Path.Combine(jobPath, "endpointB");
        
        Directory.CreateDirectory(job.ResponsePathA);
        Directory.CreateDirectory(job.ResponsePathB);

        _logger.LogInformation(
            "Starting execution of {Count} requests with max concurrency {Concurrency}",
            requests.Count,
            job.MaxConcurrency);

        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = job.MaxConcurrency,
            CancellationToken = cancellationToken
        };

        await Parallel.ForEachAsync(requests, parallelOptions, async (request, ct) =>
        {
            var result = await ExecuteSingleRequestAsync(job, request, ct).ConfigureAwait(false);
            results.Add(result);

            var completed = Interlocked.Increment(ref completedCount);
            if (completed % Math.Max(1, requests.Count / 100) == 0 || completed == requests.Count)
            {
                progress?.Report((completed, requests.Count, $"Executed {completed} of {requests.Count} requests"));
            }
        }).ConfigureAwait(false);

        return results.OrderBy(r => r.Request.RelativePath).ToList();
    }

    private async Task<RequestExecutionResult> ExecuteSingleRequestAsync(
        RequestComparisonJob job,
        RequestFileInfo request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Read request body
            await using var requestBodyStream = new FileStream(
                request.FilePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            // Merge headers (global + per-request, per-request takes precedence)
            var headersA = MergeHeaders(job.HeadersA, request.Headers);
            var headersB = MergeHeaders(job.HeadersB, request.Headers);

            var sourceRequestBody = await ReadRequestBodyAsync(requestBodyStream, cancellationToken).ConfigureAwait(false);

            // Execute both requests in parallel
            var contentTypeA = string.IsNullOrWhiteSpace(job.ContentTypeOverride)
                ? request.ContentType
                : job.ContentTypeOverride;

            var endpointBRequestBody = sourceRequestBody;
            var contentTypeB = contentTypeA;
            if (job.UseAlternateContractForEndpointB)
            {
                var alternateRequest = await _alternateContractTransformationService.PrepareEndpointBRequestAsync(
                    job,
                    request,
                    sourceRequestBody,
                    cancellationToken).ConfigureAwait(false);

                endpointBRequestBody = alternateRequest.Body;
                contentTypeB = alternateRequest.ContentType;
            }

            var (responseA, responseB) = await ExecuteBothEndpointsAsync(
                job,
                sourceRequestBody,
                contentTypeA,
                headersA,
                endpointBRequestBody,
                headersB,
                contentTypeB,
                cancellationToken).ConfigureAwait(false);

            // Generate deterministic response file paths
            var sanitizedPath = SanitizePath(request.RelativePath);
            var responsePathA = Path.Combine(job.ResponsePathA!, sanitizedPath);
            var responsePathB = Path.Combine(job.ResponsePathB!, sanitizedPath);

            // Ensure directories exist
            Directory.CreateDirectory(Path.GetDirectoryName(responsePathA)!);
            Directory.CreateDirectory(Path.GetDirectoryName(responsePathB)!);

            IReadOnlyList<MaskRuleDto> endpointAMaskRules = job.MaskRules;
            IReadOnlyList<MaskRuleDto> endpointBMaskRules = job.MaskRules;

            if (job.MaskRules.Count > 0 && job.UseAlternateContractForEndpointB)
            {
                endpointBMaskRules = _alternateContractTransformationService.GetEndpointBRawResponseMaskRules(job);
            }

            var shouldMaskResponses = job.MaskRules.Count > 0;

            var contentA = shouldMaskResponses
                ? _responseMaskingService.MaskContent(responseA.content, responseA.contentType, responsePathA, endpointAMaskRules)
                : responseA.content;
            var contentB = shouldMaskResponses
                ? _responseMaskingService.MaskContent(responseB.content, responseB.contentType, responsePathB, endpointBMaskRules)
                : responseB.content;

            // Stream responses to disk
            await SaveResponseAsync(contentA, responsePathA, cancellationToken).ConfigureAwait(false);
            await SaveResponseAsync(contentB, responsePathB, cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            return new RequestExecutionResult
            {
                Request = request,
                Success = true,
                StatusCodeA = (int)responseA.statusCode,
                StatusCodeB = (int)responseB.statusCode,
                ContentTypeA = responseA.contentType,
                ContentTypeB = responseB.contentType,
                ResponsePathA = responsePathA,
                ResponsePathB = responsePathB,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogWarning(ex, "Failed to execute request {Path}", request.RelativePath);

            return new RequestExecutionResult
            {
                Request = request,
                Success = false,
                ErrorMessage = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<((System.Net.HttpStatusCode statusCode, byte[] content, string? contentType) responseA, (System.Net.HttpStatusCode statusCode, byte[] content, string? contentType) responseB)> ExecuteBothEndpointsAsync(
        RequestComparisonJob job,
        byte[] requestBodyA,
        string contentTypeA,
        Dictionary<string, string> headersA,
        byte[] requestBodyB,
        Dictionary<string, string> headersB,
        string contentTypeB,
        CancellationToken cancellationToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromMilliseconds(job.TimeoutMs));

        var taskA = SendRequestAsync(job.EndpointA, requestBodyA, contentTypeA, headersA, cts.Token);
        var taskB = SendRequestAsync(job.EndpointB, requestBodyB, contentTypeB, headersB, cts.Token);

        await Task.WhenAll(taskA, taskB).ConfigureAwait(false);

        return (await taskA, await taskB);
    }

    private async Task<(System.Net.HttpStatusCode statusCode, byte[] content, string? contentType)> SendRequestAsync(
        Uri endpoint,
        byte[] body,
        string contentType,
        Dictionary<string, string> headers,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Content = new ByteArrayContent(body);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        foreach (var header in headers)
        {
            request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        var responseContentType = response.Content.Headers.ContentType?.ToString();

        return (response.StatusCode, content, responseContentType);
    }

    private async Task SaveResponseAsync(byte[] content, string path, CancellationToken cancellationToken)
    {
        await using var fileStream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        await fileStream.WriteAsync(content, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadRequestBodyAsync(Stream requestBodyStream, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        await requestBodyStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
        return memoryStream.ToArray();
    }

    private static Dictionary<string, string> MergeHeaders(
        IReadOnlyDictionary<string, string> globalHeaders,
        IReadOnlyDictionary<string, string> requestHeaders)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in globalHeaders)
        {
            merged[header.Key] = header.Value;
        }

        // Per-request headers override global headers
        foreach (var header in requestHeaders)
        {
            merged[header.Key] = header.Value;
        }

        return merged;
    }

    private static string SanitizePath(string relativePath)
    {
        // Remove any path traversal attempts and normalize separators
        var sanitized = relativePath
            .Replace("..", "_")
            .Replace('/', Path.DirectorySeparatorChar)
            .Replace('\\', Path.DirectorySeparatorChar);

        // Ensure the path doesn't start with a separator
        return sanitized.TrimStart(Path.DirectorySeparatorChar);
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
