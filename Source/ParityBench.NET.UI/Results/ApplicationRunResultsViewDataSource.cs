using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Results;

public sealed class ApplicationRunResultsViewDataSource : IRunResultsViewDataSource
{
    private readonly IComparisonRunResultUseCases resultUseCases;

    public ApplicationRunResultsViewDataSource(IComparisonRunResultUseCases resultUseCases)
    {
        this.resultUseCases = resultUseCases;
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        resultUseCases.ListRunsAsync(cancellationToken);

    public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunAsync(runId, cancellationToken);

    public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunSummaryAsync(runId, cancellationToken);

    public Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunDetailsAsync(runId, query, cancellationToken);

    public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default) =>
        resultUseCases.ReadArtifactPreviewAsync(artifact, maxBytes, cancellationToken);
}