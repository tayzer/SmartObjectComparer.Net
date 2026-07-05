using System.Text;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
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

    public async Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        ComparisonRun run = await LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (run.Summary?.DetailIndexReference is null)
        {
            return new RunDetailPage(Array.Empty<ParityBench.NET.Domain.Requests.RequestPairResult>(), 0, query.Offset, query.Limit);
        }

        return await runDetailStore
            .LoadPageAsync(run.Summary.DetailIndexReference, query, cancellationToken)
            .ConfigureAwait(false);
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
}