using System.Text;
using System.Text.Json;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Results;

public sealed class ApplicationRunResultsViewDataSource : IRunResultsViewDataSource
{
    private readonly IComparisonRunResultUseCases resultUseCases;
    private readonly IComparisonRunUseCases runUseCases;
    private readonly IBaselineStore? baselineStore;

    public ApplicationRunResultsViewDataSource(
        IComparisonRunResultUseCases resultUseCases,
        IComparisonRunUseCases runUseCases,
        IBaselineStore? baselineStore = null)
    {
        this.resultUseCases = resultUseCases;
        this.runUseCases = runUseCases;
        this.baselineStore = baselineStore;
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        resultUseCases.ListRunsAsync(cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage = null,
        CancellationToken cancellationToken = default) =>
        runUseCases.CancelRunAsync(runId, cancellationMessage, cancellationToken);

    public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunAsync(runId, cancellationToken);

    public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunSummaryAsync(runId, cancellationToken);

    public async Task<StaticReportMetadata?> LoadReportMetadataAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await resultUseCases.LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        BaselineReportProvenance? baseline = await BaselineProvenanceFactory
            .CreateAsync(baselineStore, run, cancellationToken)
            .ConfigureAwait(false);

        return StaticReportMetadata.FromRun(run, DateTimeOffset.UtcNow, baseline);
    }

    public Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        resultUseCases.LoadReportAnalysisAsync(runId, cancellationToken);

    public async Task<StaticReportDifferenceIndex> LoadDifferenceIndexAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        await resultUseCases.LoadDifferenceIndexAsync(runId, cancellationToken).ConfigureAwait(false)
            ?? new StaticReportDifferenceIndex(0, 0);

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

    public Task<ArtifactContentPreview> ReadArtifactContentAsync(
        ArtifactReference artifact,
        int maxBytes = 512 * 1024,
        CancellationToken cancellationToken = default) =>
        resultUseCases.ReadArtifactPreviewAsync(artifact, maxBytes, cancellationToken);

    public async Task<string> ExportRunDetailsJsonAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new MemoryStream();
        await resultUseCases.ExportRunDetailsJsonAsync(runId, stream, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public async Task<string> ExportRunDetailsCsvAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        using MemoryStream stream = new MemoryStream();
        await resultUseCases.ExportRunDetailsCsvAsync(runId, stream, cancellationToken).ConfigureAwait(false);
        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
