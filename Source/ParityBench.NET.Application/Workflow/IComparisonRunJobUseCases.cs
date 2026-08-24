using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Workflow;

/// <summary>
/// Starts and tracks in-process run jobs for interactive hosts.
/// </summary>
public interface IComparisonRunJobUseCases
{
    /// <summary>
    /// Starts a run in the background. Returns false when the run is already active.
    /// </summary>
    Task<bool> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Requests cancellation for a run.
    /// </summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>Requests cancellation with a supplied lifecycle message.</summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default) =>
        CancelRunAsync(runId, cancellationToken);

    /// <summary>
    /// Returns true when a run is currently active in this job tracker.
    /// </summary>
    bool IsRunning(RunId runId);
}
