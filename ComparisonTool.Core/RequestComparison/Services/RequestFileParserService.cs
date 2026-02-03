using System.Text.Json;
using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Service for parsing request files and their associated sidecar headers.
/// </summary>
public class RequestFileParserService
{
    private readonly ILogger<RequestFileParserService> _logger;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Content type mapping by file extension.
    /// </summary>
    private static readonly Dictionary<string, string> ContentTypeMap = new(StringComparer.OrdinalIgnoreCase)
    {
        { ".json", "application/json" },
        { ".xml", "application/xml" },
        { ".txt", "text/plain" },
        { ".html", "text/html" },
        { ".htm", "text/html" }
    };

    public RequestFileParserService(ILogger<RequestFileParserService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Parses all request files from a batch.
    /// </summary>
    public async Task<IReadOnlyList<RequestFileInfo>> ParseRequestBatchAsync(
        string batchId,
        CancellationToken cancellationToken = default)
    {
        var batchPath = GetBatchPath(batchId);

        if (!Directory.Exists(batchPath))
        {
            throw new DirectoryNotFoundException($"Request batch {batchId} not found at {batchPath}");
        }

        var requests = new List<RequestFileInfo>();

        // Get all files except sidecar header files
        var files = Directory.GetFiles(batchPath, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase))
            .Where(f => !Path.GetFileName(f).StartsWith("_"))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToList();

        foreach (var filePath in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var request = await ParseRequestFileAsync(batchPath, filePath, cancellationToken)
                .ConfigureAwait(false);
            requests.Add(request);
        }

        _logger.LogInformation("Parsed {Count} request files from batch {BatchId}", requests.Count, batchId);
        return requests;
    }

    /// <summary>
    /// Parses a single request file with optional sidecar headers.
    /// </summary>
    private async Task<RequestFileInfo> ParseRequestFileAsync(
        string batchPath,
        string filePath,
        CancellationToken cancellationToken)
    {
        var relativePath = Path.GetRelativePath(batchPath, filePath);
        var fileInfo = new FileInfo(filePath);
        var extension = fileInfo.Extension.ToLowerInvariant();
        var contentType = GetContentType(extension);

        // Check for sidecar header file
        var headers = await LoadSidecarHeadersAsync(filePath, cancellationToken)
            .ConfigureAwait(false);

        return new RequestFileInfo
        {
            RelativePath = relativePath,
            FilePath = filePath,
            ContentType = contentType,
            Headers = headers,
            FileSize = fileInfo.Length
        };
    }

    /// <summary>
    /// Loads per-request headers from a sidecar file if it exists.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> LoadSidecarHeadersAsync(
        string requestFilePath,
        CancellationToken cancellationToken)
    {
        var sidecarPath = requestFilePath + ".headers.json";

        if (!File.Exists(sidecarPath))
        {
            return new Dictionary<string, string>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(sidecarPath, cancellationToken)
                .ConfigureAwait(false);

            var sidecar = JsonSerializer.Deserialize<RequestHeadersSidecar>(json, JsonOptions);
            return sidecar?.Headers ?? new Dictionary<string, string>();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Failed to parse sidecar headers from {Path}, using empty headers",
                sidecarPath);
            return new Dictionary<string, string>();
        }
    }

    /// <summary>
    /// Gets the content type for a file extension.
    /// </summary>
    public static string GetContentType(string extension)
    {
        if (string.IsNullOrEmpty(extension))
        {
            return "text/plain";
        }

        return ContentTypeMap.TryGetValue(extension, out var contentType)
            ? contentType
            : "text/plain";
    }

    /// <summary>
    /// Gets the batch storage path.
    /// </summary>
    private static string GetBatchPath(string batchId)
    {
        return Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
    }
}
