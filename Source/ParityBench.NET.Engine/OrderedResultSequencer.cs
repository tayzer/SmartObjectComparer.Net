using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine;

internal sealed class OrderedResultSequencer
{
    private readonly IRunDetailWriter detailWriter;
    private readonly RunSummaryAccumulator summaryAccumulator;
    private readonly List<ComparedExecutionRecord> persistedRecords;
    private readonly int flushBatchSize;
    private readonly Action<TimeSpan> recordPersistenceElapsed;
    private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);
    private readonly Dictionary<int, ComparedExecutionRecord> pending = new Dictionary<int, ComparedExecutionRecord>();
    private readonly List<ComparedExecutionRecord> readyBuffer = new List<ComparedExecutionRecord>();
    private int nextOrdinal;

    public OrderedResultSequencer(
        IRunDetailWriter detailWriter,
        RunSummaryAccumulator summaryAccumulator,
        List<ComparedExecutionRecord> persistedRecords,
        int flushBatchSize,
        Action<TimeSpan> recordPersistenceElapsed)
    {
        this.detailWriter = detailWriter;
        this.summaryAccumulator = summaryAccumulator;
        this.persistedRecords = persistedRecords;
        this.flushBatchSize = flushBatchSize;
        this.recordPersistenceElapsed = recordPersistenceElapsed;
    }

    public async Task SubmitAsync(ComparedExecutionRecord record, CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            pending[record.ManifestOrdinal] = record;
            while (pending.TryGetValue(nextOrdinal, out ComparedExecutionRecord? next))
            {
                pending.Remove(nextOrdinal);
                readyBuffer.Add(next);
                nextOrdinal++;
            }

            if (readyBuffer.Count >= flushBatchSize)
            {
                await FlushLockedAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public async Task FlushRemainingAsync(CancellationToken cancellationToken)
    {
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await FlushLockedAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task FlushLockedAsync(CancellationToken cancellationToken)
    {
        if (readyBuffer.Count == 0)
        {
            return;
        }

        Stopwatch persistStopwatch = Stopwatch.StartNew();
        IReadOnlyList<RequestPairResult> resultsToPersist = readyBuffer
            .Select(record => record.Result)
            .ToList();
        summaryAccumulator.Add(resultsToPersist);
        persistedRecords.AddRange(readyBuffer);
        await detailWriter.AppendAsync(resultsToPersist, cancellationToken).ConfigureAwait(false);
        persistStopwatch.Stop();
        recordPersistenceElapsed(persistStopwatch.Elapsed);

        readyBuffer.Clear();
    }
}
