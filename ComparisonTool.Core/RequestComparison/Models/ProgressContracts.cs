namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Represents a comparison phase for progress tracking.
/// </summary>
public enum ComparisonPhase
{
    /// <summary>Job is initializing.</summary>
    Initializing,

    /// <summary>Parsing request files.</summary>
    Parsing,

    /// <summary>Executing requests against endpoints.</summary>
    Executing,

    /// <summary>Comparing responses.</summary>
    Comparing,

    /// <summary>Job completed successfully.</summary>
    Completed,

    /// <summary>Job failed with an error.</summary>
    Failed,

    /// <summary>Job was cancelled.</summary>
    Cancelled
}

/// <summary>
/// Progress update DTO for SignalR broadcasting.
/// </summary>
public record ComparisonProgressUpdate
{
    /// <summary>Gets the job ID.</summary>
    public required string JobId { get; init; }

    /// <summary>Gets the current phase.</summary>
    public required ComparisonPhase Phase { get; init; }

    /// <summary>Gets the percent complete (0-100).</summary>
    public required int PercentComplete { get; init; }

    /// <summary>Gets the status message.</summary>
    public required string Message { get; init; }

    /// <summary>Gets the timestamp of the update.</summary>
    public required DateTimeOffset Timestamp { get; init; }

    /// <summary>Gets the number of completed items (optional).</summary>
    public int? CompletedItems { get; init; }

    /// <summary>Gets the total number of items (optional).</summary>
    public int? TotalItems { get; init; }

    /// <summary>Gets the error message if failed (optional).</summary>
    public string? ErrorMessage { get; init; }
}
