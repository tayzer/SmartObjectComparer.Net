namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Represents the status of a request comparison job.
/// </summary>
public enum RequestComparisonStatus
{
    /// <summary>Job is created but not yet started.</summary>
    Pending,

    /// <summary>Job is currently uploading requests.</summary>
    Uploading,

    /// <summary>Job is executing requests against endpoints.</summary>
    Executing,

    /// <summary>Job is comparing responses.</summary>
    Comparing,

    /// <summary>Job is performing analysis.</summary>
    Analyzing,

    /// <summary>Job completed successfully.</summary>
    Completed,

    /// <summary>Job failed with errors.</summary>
    Failed,

    /// <summary>Job was cancelled by user.</summary>
    Cancelled
}

/// <summary>
/// Represents a request comparison job configuration.
/// </summary>
public record RequestComparisonJob
{
    /// <summary>Gets the unique job identifier.</summary>
    public required string JobId { get; init; }

    /// <summary>Gets the request batch identifier.</summary>
    public required string RequestBatchId { get; init; }

    /// <summary>Gets the first endpoint URL.</summary>
    public required Uri EndpointA { get; init; }

    /// <summary>Gets the second endpoint URL.</summary>
    public required Uri EndpointB { get; init; }

    /// <summary>Gets the headers for endpoint A.</summary>
    public IReadOnlyDictionary<string, string> HeadersA { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the headers for endpoint B.</summary>
    public IReadOnlyDictionary<string, string> HeadersB { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the timeout in milliseconds for each request.</summary>
    public int TimeoutMs { get; init; } = 30000;

    /// <summary>Gets the maximum concurrent requests.</summary>
    public int MaxConcurrency { get; init; } = 64;

    /// <summary>Gets the job creation timestamp.</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>Gets or sets the current job status.</summary>
    public RequestComparisonStatus Status { get; set; } = RequestComparisonStatus.Pending;

    /// <summary>Gets or sets the total number of requests.</summary>
    public int TotalRequests { get; set; }

    /// <summary>Gets or sets the number of completed requests.</summary>
    public int CompletedRequests { get; set; }

    /// <summary>Gets or sets the status message.</summary>
    public string? StatusMessage { get; set; }

    /// <summary>Gets or sets the error message if failed.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the path to response folder A.</summary>
    public string? ResponsePathA { get; set; }

    /// <summary>Gets or sets the path to response folder B.</summary>
    public string? ResponsePathB { get; set; }

    /// <summary>Gets or sets the model name for comparison.</summary>
    public string ModelName { get; set; } = "Auto";
}
