using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Results;

/// <summary>
/// Supplies result-view data to shared V2 UI components without binding them to a host or workspace implementation.
/// </summary>
public interface IRunResultsViewDataSource
{
    Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default);

    Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<StaticReportMetadata?> LoadReportMetadataAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<StaticReportDifferenceIndex> LoadDifferenceIndexAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default);

    Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default);

    Task<ArtifactContentPreview> ReadArtifactContentAsync(
        ArtifactReference artifact,
        int maxBytes = 512 * 1024,
        CancellationToken cancellationToken = default);

    Task<string> ExportRunDetailsJsonAsync(RunId runId, CancellationToken cancellationToken = default);

    Task<string> ExportRunDetailsCsvAsync(RunId runId, CancellationToken cancellationToken = default);
}
