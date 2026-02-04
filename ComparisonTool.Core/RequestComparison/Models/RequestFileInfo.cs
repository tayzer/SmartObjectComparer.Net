namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Represents metadata about a request file including headers.
/// </summary>
public record RequestFileInfo
{
    /// <summary>Gets the relative path of the request file (used as logical ID).</summary>
    public required string RelativePath { get; init; }

    /// <summary>Gets the absolute file path on disk.</summary>
    public required string FilePath { get; init; }

    /// <summary>Gets the content type inferred from file extension.</summary>
    public required string ContentType { get; init; }

    /// <summary>Gets the per-request headers from sidecar file.</summary>
    public IReadOnlyDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the file size in bytes.</summary>
    public long FileSize { get; init; }
}

/// <summary>
/// Represents per-request headers loaded from a sidecar file.
/// </summary>
public record RequestHeadersSidecar
{
    /// <summary>Gets the headers dictionary.</summary>
    public Dictionary<string, string> Headers { get; init; } = new();
}

/// <summary>
/// Represents the result of executing a single request.
/// </summary>
public record RequestExecutionResult
{
    /// <summary>Gets the request file info.</summary>
    public required RequestFileInfo Request { get; init; }

    /// <summary>Gets whether the request succeeded.</summary>
    public bool Success { get; init; }

    /// <summary>Gets the HTTP status code from endpoint A.</summary>
    public int StatusCodeA { get; init; }

    /// <summary>Gets the HTTP status code from endpoint B.</summary>
    public int StatusCodeB { get; init; }

    /// <summary>Gets the response file path for endpoint A.</summary>
    public string? ResponsePathA { get; init; }

    /// <summary>Gets the response file path for endpoint B.</summary>
    public string? ResponsePathB { get; init; }

    /// <summary>Gets the error message if failed.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Gets the execution duration in milliseconds.</summary>
    public long DurationMs { get; init; }
}
