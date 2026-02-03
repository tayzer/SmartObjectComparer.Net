using System.ComponentModel.DataAnnotations;

namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Request body for creating a new request comparison job.
/// </summary>
public record CreateRequestComparisonJobRequest
{
    /// <summary>Gets the batch ID from the request upload.</summary>
    [Required]
    public required string RequestBatchId { get; init; }

    /// <summary>Gets the first endpoint URL.</summary>
    [Required]
    [Url]
    public required string EndpointA { get; init; }

    /// <summary>Gets the second endpoint URL.</summary>
    [Required]
    [Url]
    public required string EndpointB { get; init; }

    /// <summary>Gets the headers for endpoint A.</summary>
    public Dictionary<string, string>? HeadersA { get; init; }

    /// <summary>Gets the headers for endpoint B.</summary>
    public Dictionary<string, string>? HeadersB { get; init; }

    /// <summary>Gets the timeout in milliseconds for each request.</summary>
    [Range(1000, 300000)]
    public int TimeoutMs { get; init; } = 30000;

    /// <summary>Gets the maximum concurrent requests.</summary>
    [Range(1, 256)]
    public int MaxConcurrency { get; init; } = 64;

    /// <summary>Gets the model name to use for comparison.</summary>
    public string? ModelName { get; init; }
}

/// <summary>
/// Response for job creation.
/// </summary>
public record CreateJobResponse
{
    /// <summary>Gets the created job ID.</summary>
    public required string JobId { get; init; }
}

/// <summary>
/// Response for job status.
/// </summary>
public record JobStatusResponse
{
    /// <summary>Gets the job status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets the number of completed requests.</summary>
    public int Completed { get; init; }

    /// <summary>Gets the total number of requests.</summary>
    public int Total { get; init; }

    /// <summary>Gets the status message.</summary>
    public string? Message { get; init; }

    /// <summary>Gets the error message if failed.</summary>
    public string? Error { get; init; }
}

/// <summary>
/// Response for batch upload.
/// </summary>
public record RequestBatchUploadResponse
{
    /// <summary>Gets the number of uploaded files.</summary>
    public int Uploaded { get; init; }

    /// <summary>Gets the batch ID for large uploads.</summary>
    public string? BatchId { get; init; }

    /// <summary>Gets the list of files for small uploads.</summary>
    public List<string>? Files { get; init; }

    /// <summary>Gets the path to the file list for large uploads.</summary>
    public string? FileListPath { get; init; }
}
