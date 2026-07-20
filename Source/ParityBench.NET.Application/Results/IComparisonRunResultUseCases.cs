using System.Text;
using System.Text.Json;

using ParityBench.NET.Domain.Reports;
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

    Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StaticReportAnalysisSnapshot?>(null);

    Task<StaticReportDifferenceIndex?> LoadDifferenceIndexAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<StaticReportDifferenceIndex?>(null);

    /// <summary>
    /// Loads one page of pair details without loading raw response bodies.
    /// </summary>
    Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default);

    async Task ExportRunDetailsJsonAsync(
        RunId runId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        await using Utf8JsonWriter writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        int offset = 0;
        while (true)
        {
            RunDetailPage page = await LoadRunDetailsAsync(runId, new RunDetailQuery(offset, RunDetailQuery.MaxLimit), cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                JsonSerializer.Serialize(writer, item, StaticReportJsonOptions.Create());
            }

            if (!page.HasMore)
            {
                break;
            }

            offset += page.Items.Count;
        }

        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    async Task ExportRunDetailsCsvAsync(
        RunId runId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        using StreamWriter writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("Request,Outcome,Differences,StatusA,StatusB,Error").ConfigureAwait(false);
        int offset = 0;
        while (true)
        {
            RunDetailPage page = await LoadRunDetailsAsync(runId, new RunDetailQuery(offset, RunDetailQuery.MaxLimit), cancellationToken).ConfigureAwait(false);
            foreach (var item in page.Items)
            {
                await writer.WriteLineAsync($"{item.RelativePath},{item.Outcome},{item.DifferenceCount},{item.ResponseA?.StatusCode.ToString() ?? string.Empty},{item.ResponseB?.StatusCode.ToString() ?? string.Empty},{item.ErrorMessage ?? item.OutcomeMessage ?? string.Empty}").ConfigureAwait(false);
            }

            if (!page.HasMore)
            {
                break;
            }

            offset += page.Items.Count;
        }
    }

    /// <summary>
    /// Reads a bounded text preview of a response artifact.
    /// </summary>
    Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default);
}
