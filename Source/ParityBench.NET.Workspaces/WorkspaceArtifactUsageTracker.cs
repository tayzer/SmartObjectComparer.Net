using System.Threading;

namespace ParityBench.NET.Workspaces;

/// <summary>
/// Keeps a process-local artifact byte total. The first retention pass reconciles
/// from disk; later saves/deletes update the total atomically, avoiding a full
/// workspace walk for every run.
/// </summary>
public sealed class WorkspaceArtifactUsageTracker
{
    private readonly SemaphoreSlim initializationGate = new(1, 1);
    private long totalBytes;
    private int initialized;

    public async Task<long> GetTotalAsync(Func<CancellationToken, Task<long>> reconcileAsync, CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref initialized) == 1)
        {
            return Interlocked.Read(ref totalBytes);
        }

        await initializationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initialized == 0)
            {
                Interlocked.Exchange(ref totalBytes, await reconcileAsync(cancellationToken).ConfigureAwait(false));
                Volatile.Write(ref initialized, 1);
            }

            return Interlocked.Read(ref totalBytes);
        }
        finally
        {
            initializationGate.Release();
        }
    }

    public void RecordDelta(long delta)
    {
        if (Volatile.Read(ref initialized) == 1)
        {
            Interlocked.Add(ref totalBytes, delta);
        }
    }
}
