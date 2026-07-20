using System.Text;
using System.Text.Json;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Results;

public sealed class ComparisonRunResultService : IComparisonRunResultUseCases
{
    public const int DefaultPreviewBytes = 64 * 1024;

    public const int MaxPreviewBytes = 1024 * 1024;

    private readonly IRunStore runStore;
    private readonly IRunDetailStore runDetailStore;
    private readonly IRunArtifactStore runArtifactStore;

    public ComparisonRunResultService(
        IRunStore runStore,
        IRunDetailStore runDetailStore,
        IRunArtifactStore runArtifactStore)
    {
        this.runStore = runStore;
        this.runDetailStore = runDetailStore;
        this.runArtifactStore = runArtifactStore;
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        runStore.ListAsync(cancellationToken);

    public async Task<ComparisonRun> LoadRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun? run = await runStore.LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        return run ?? throw new RunNotFoundException(runId);
    }

    public async Task<RunResultSummary?> LoadRunSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        return run.Summary;
    }

    public async Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.Summary?.DetailIndexReference is null)
        {
            return null;
        }

        return await runDetailStore.LoadAnalysisAsync(run.Summary.DetailIndexReference, cancellationToken).ConfigureAwait(false);
    }

    public async Task<StaticReportDifferenceIndex?> LoadDifferenceIndexAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.Summary?.DetailIndexReference is null)
        {
            return null;
        }

        return await runDetailStore.LoadDifferenceIndexAsync(run.Summary.DetailIndexReference, cancellationToken).ConfigureAwait(false);
    }

    public async Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ComparisonRun run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.Summary?.DetailIndexReference is null)
        {
            return new RunDetailPage(Array.Empty<RequestPairResult>(), 0, query.Offset, query.Limit);
        }

        return await runDetailStore
            .LoadPageAsync(run.Summary.DetailIndexReference, query, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task ExportRunDetailsJsonAsync(
        RunId runId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        await using Utf8JsonWriter writer = new Utf8JsonWriter(destination, new JsonWriterOptions { Indented = true });
        writer.WriteStartArray();
        await ForEachDetailPageAsync(
            runId,
            async page =>
            {
                foreach (RequestPairResult item in page.Items)
                {
                    JsonSerializer.Serialize(writer, item, StaticReportJsonOptions.Create());
                }

                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
        writer.WriteEndArray();
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task ExportRunDetailsCsvAsync(
        RunId runId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);

        using StreamWriter writer = new StreamWriter(destination, new UTF8Encoding(false), leaveOpen: true);
        await writer.WriteLineAsync("Request,Outcome,Differences,StatusA,StatusB,Error").ConfigureAwait(false);
        await ForEachDetailPageAsync(
            runId,
            async page =>
            {
                foreach (RequestPairResult item in page.Items)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await writer.WriteLineAsync(BuildCsvRow(item)).ConfigureAwait(false);
                }

                await writer.FlushAsync().ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = DefaultPreviewBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (maxBytes is < 1 or > MaxPreviewBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), $"Preview size must be between 1 and {MaxPreviewBytes} bytes.");
        }

        await using Stream stream = await OpenArtifactAsync(artifact, cancellationToken).ConfigureAwait(false);
        long? totalLength = stream.CanSeek ? stream.Length : null;
        byte[] buffer = new byte[maxBytes + 1];
        int totalRead = 0;

        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream
                .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        bool isTruncated = totalRead > maxBytes || totalLength > maxBytes;
        int previewBytes = Math.Min(totalRead, maxBytes);
        string content = Encoding.UTF8.GetString(buffer, 0, previewBytes);

        return new ArtifactContentPreview(
            artifact,
            content,
            previewBytes,
            isTruncated,
            artifact.ContentType,
            totalLength);
    }

    private async Task ForEachDetailPageAsync(
        RunId runId,
        Func<RunDetailPage, Task> handlePageAsync,
        CancellationToken cancellationToken)
    {
        int offset = 0;
        while (true)
        {
            RunDetailPage page = await LoadRunDetailsAsync(runId, new RunDetailQuery(offset, RunDetailQuery.MaxLimit), cancellationToken).ConfigureAwait(false);
            if (page.Items.Count == 0)
            {
                break;
            }

            await handlePageAsync(page).ConfigureAwait(false);
            if (!page.HasMore)
            {
                break;
            }

            offset += page.Items.Count;
        }
    }

    private async Task<Stream> OpenArtifactAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken)
    {
        try
        {
            return await runArtifactStore.OpenReadAsync(artifact, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            throw new ArtifactNotFoundException(artifact, ex);
        }
    }

    private static string BuildCsvRow(RequestPairResult item) =>
        string.Join(",", new[]
        {
            EscapeCsv(item.RelativePath),
            EscapeCsv(item.Outcome.ToString()),
            item.DifferenceCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            item.ResponseA?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            item.ResponseB?.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
            EscapeCsv(item.ErrorMessage ?? item.OutcomeMessage ?? string.Empty),
        });

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
