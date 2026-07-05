using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Results;

/// <summary>
/// Reads historical comparison run results without executing or mutating runs.
/// </summary>
public interface IComparisonRunResultUseCases
{
    /// <summary>
    /// Lists cheap run snapshots for historical browsing.
    /// </summary>
    Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a full run snapshot by identifier.
    /// </summary>
    Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads the count-only result summary for a run.
    /// </summary>
    Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads one page of pair details without loading raw response bodies.
    /// </summary>
    Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads a bounded text preview of a response artifact.
    /// </summary>
    Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default);
}