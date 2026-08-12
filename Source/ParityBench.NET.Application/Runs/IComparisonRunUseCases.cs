using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Defines host-facing use cases for managing comparison runs.
/// </summary>
public interface IComparisonRunUseCases
{
    /// <summary>
    /// Creates a run with immutable options and stores it in the Created state.
    /// </summary>
    Task<ComparisonRun> CreateRunAsync(
        RunOptions options,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a stored run and advances it through executor-reported lifecycle states.
    /// </summary>
    Task<ComparisonRun> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a non-terminal run.
    /// </summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a non-terminal run and records an operator- or host-supplied reason.
    /// </summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default) =>
        CancelRunAsync(runId, cancellationToken);

    /// <summary>
    /// Lists cheap run snapshots without loading raw response details.
    /// </summary>
    Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a count-only run result summary when one exists.
    /// </summary>
    Task<RunResultSummary?> LoadRunSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default);
}
