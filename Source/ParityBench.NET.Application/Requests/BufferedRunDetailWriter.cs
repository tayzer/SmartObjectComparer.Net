using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

public sealed class BufferedRunDetailWriter : IRunDetailWriter
{
    private readonly IRunDetailStore store;
    private readonly RunId runId;
    private readonly List<RequestPairResult> results = new List<RequestPairResult>();
    private bool completed;

    public BufferedRunDetailWriter(IRunDetailStore store, RunId runId)
    {
        this.store = store;
        this.runId = runId;
    }

    public Task AppendAsync(
        IReadOnlyList<RequestPairResult> results,
        CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            throw new InvalidOperationException("Cannot append run details after completion.");
        }

        this.results.AddRange(results);
        return Task.CompletedTask;
    }

    public async Task<RunDetailReference> CompleteAsync(CancellationToken cancellationToken = default)
    {
        if (completed)
        {
            throw new InvalidOperationException("Run detail writer has already completed.");
        }

        completed = true;
        return await store.SaveDetailsAsync(runId, results, cancellationToken).ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
