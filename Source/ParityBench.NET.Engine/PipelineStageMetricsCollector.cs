using System.Diagnostics;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

internal sealed class PipelineStageMetricsCollector
{
    private readonly StageQueueCounter executeToMapping = new();
    private readonly StageQueueCounter mappingToComparison = new();
    private readonly StageQueueCounter comparisonToFocused = new();
    private long mappingWorkerTicks;
    private long comparisonWorkerTicks;
    private long focusedWorkerTicks;

    public StageQueueCounter ExecuteToMapping => executeToMapping;
    public StageQueueCounter MappingToComparison => mappingToComparison;
    public StageQueueCounter ComparisonToFocused => comparisonToFocused;

    public void AddMappingWorker(TimeSpan elapsed) => Interlocked.Add(ref mappingWorkerTicks, elapsed.Ticks);
    public void AddComparisonWorker(TimeSpan elapsed) => Interlocked.Add(ref comparisonWorkerTicks, elapsed.Ticks);
    public void AddFocusedWorker(TimeSpan elapsed) => Interlocked.Add(ref focusedWorkerTicks, elapsed.Ticks);

    public PipelineStageMetrics Snapshot(
        int mappingConcurrency,
        int comparisonConcurrency,
        int focusedContentConcurrency,
        int executeToMappingCapacity,
        int mappingToComparisonCapacity,
        int comparisonToFocusedCapacity,
        TimeSpan detailPersistenceDuration) => new(
            mappingConcurrency,
            comparisonConcurrency,
            focusedContentConcurrency,
            executeToMappingCapacity,
            mappingToComparisonCapacity,
            comparisonToFocusedCapacity,
            TimeSpan.FromTicks(Interlocked.Read(ref mappingWorkerTicks)),
            TimeSpan.FromTicks(Interlocked.Read(ref comparisonWorkerTicks)),
            TimeSpan.FromTicks(Interlocked.Read(ref focusedWorkerTicks)),
            detailPersistenceDuration,
            executeToMapping.QueueWait,
            mappingToComparison.QueueWait,
            comparisonToFocused.QueueWait,
            executeToMapping.Backpressure,
            mappingToComparison.Backpressure,
            comparisonToFocused.Backpressure,
            Math.Min(executeToMappingCapacity, executeToMapping.MaximumDepth),
            Math.Min(mappingToComparisonCapacity, mappingToComparison.MaximumDepth),
            Math.Min(comparisonToFocusedCapacity, comparisonToFocused.MaximumDepth));
}

internal sealed class StageQueueCounter
{
    private long queueWaitTicks;
    private long backpressureTicks;
    private int depth;
    private int maximumDepth;

    public TimeSpan QueueWait => TimeSpan.FromTicks(Interlocked.Read(ref queueWaitTicks));
    public TimeSpan Backpressure => TimeSpan.FromTicks(Interlocked.Read(ref backpressureTicks));
    public int MaximumDepth => Volatile.Read(ref maximumDepth);

    public void Enqueued()
    {
        int current = Interlocked.Increment(ref depth);
        int observed;
        while (current > (observed = Volatile.Read(ref maximumDepth))
            && Interlocked.CompareExchange(ref maximumDepth, current, observed) != observed)
        {
        }
    }

    public void EnqueueRejected() => Interlocked.Decrement(ref depth);

    public void Dequeued(long enqueuedTimestamp)
    {
        Interlocked.Decrement(ref depth);
        Interlocked.Add(ref queueWaitTicks, Stopwatch.GetElapsedTime(enqueuedTimestamp).Ticks);
    }

    public void AddBackpressure(TimeSpan elapsed) => Interlocked.Add(ref backpressureTicks, elapsed.Ticks);
}

internal readonly record struct QueuedStageRecord<T>(T Record, long EnqueuedTimestamp);
