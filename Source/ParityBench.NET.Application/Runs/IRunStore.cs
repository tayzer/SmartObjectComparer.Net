using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Stores and reads comparison-run snapshots behind an Application port.
/// </summary>
public interface IRunStore
{
    /// <summary>
    /// Saves the latest immutable run snapshot.
    /// </summary>
    Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a run snapshot by identifier.
    /// </summary>
    Task<ComparisonRun?> LoadAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists cheap run snapshots without loading pair details or raw bodies.
    /// </summary>
    Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the count-only result summary for a run when available.
    /// </summary>
    Task<RunResultSummary?> LoadSummaryAsync(RunId runId, CancellationToken cancellationToken = default);
}
