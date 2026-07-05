using System.Collections.Concurrent;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure;

public sealed class InMemoryRunCancellationRegistry : IRunCancellationRegistry
{
    private readonly ConcurrentDictionary<RunId, CancellationTokenSource> activeRuns = new ConcurrentDictionary<RunId, CancellationTokenSource>();

    public CancellationToken CreateLinkedToken(RunId runId, CancellationToken cancellationToken)
    {
        CancellationTokenSource linkedSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (activeRuns.TryAdd(runId, linkedSource))
        {
            return linkedSource.Token;
        }

        linkedSource.Dispose();
        throw new InvalidOperationException($"Run '{runId}' already has an active cancellation registration.");
    }

    public bool RequestCancellation(RunId runId)
    {
        if (!activeRuns.TryGetValue(runId, out CancellationTokenSource? source))
        {
            return false;
        }

        source.Cancel();
        return true;
    }

    public bool IsCancellationRequested(RunId runId) =>
        activeRuns.TryGetValue(runId, out CancellationTokenSource? source) && source.IsCancellationRequested;

    public void Complete(RunId runId)
    {
        if (activeRuns.TryRemove(runId, out CancellationTokenSource? source))
        {
            source.Dispose();
        }
    }
}
