using System.Diagnostics;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

internal interface IRunProcessMetricsSource
{
    ProcessResourceSample Capture();
}

internal readonly record struct ProcessResourceSample(TimeSpan CpuTime, long WorkingSetBytes, long PrivateBytes);

internal sealed class CurrentProcessMetricsSource : IRunProcessMetricsSource
{
    public ProcessResourceSample Capture()
    {
        using Process process = Process.GetCurrentProcess();
        process.Refresh();
        return new ProcessResourceSample(process.TotalProcessorTime, process.WorkingSet64, process.PrivateMemorySize64);
    }
}

/// <summary>
/// Per-run process sampler. Values are process-local and portable; no OS-wide
/// counters or Task Manager percentages are persisted.
/// </summary>
internal sealed class RunProcessResourceSampler : IAsyncDisposable
{
    private readonly IRunProcessMetricsSource source;
    private readonly int logicalProcessorCount;
    private readonly CancellationTokenSource cancellation = new();
    private readonly Stopwatch elapsed = new();
    private Task? samplingTask;
    private ProcessResourceSample initial;
    private long initialAllocatedBytes;
    private int initialGen0;
    private int initialGen1;
    private int initialGen2;
    private long peakWorkingSet;
    private long peakPrivate;
    private bool started;

    public RunProcessResourceSampler(
        IRunProcessMetricsSource? source = null,
        int? logicalProcessorCount = null)
    {
        this.source = source ?? new CurrentProcessMetricsSource();
        this.logicalProcessorCount = Math.Max(1, logicalProcessorCount ?? Environment.ProcessorCount);
    }

    public void Start()
    {
        if (started)
        {
            throw new InvalidOperationException("The process resource sampler has already started.");
        }

        started = true;
        initial = source.Capture();
        peakWorkingSet = initial.WorkingSetBytes;
        peakPrivate = initial.PrivateBytes;
        initialAllocatedBytes = GC.GetTotalAllocatedBytes(precise: false);
        initialGen0 = GC.CollectionCount(0);
        initialGen1 = GC.CollectionCount(1);
        initialGen2 = GC.CollectionCount(2);
        elapsed.Start();
        samplingTask = SampleLoopAsync();
    }

    public async Task<RunProcessResourceMetrics> StopAsync()
    {
        if (!started)
        {
            throw new InvalidOperationException("The process resource sampler has not started.");
        }

        cancellation.Cancel();
        if (samplingTask is not null)
        {
            try
            {
                await samplingTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // Expected shutdown.
            }
        }

        CapturePeak();
        elapsed.Stop();
        TimeSpan cpuDuration = source.Capture().CpuTime - initial.CpuTime;
        if (cpuDuration < TimeSpan.Zero)
        {
            cpuDuration = TimeSpan.Zero;
        }

        double corePercent = elapsed.Elapsed <= TimeSpan.Zero
            ? 0
            : cpuDuration.TotalSeconds / elapsed.Elapsed.TotalSeconds * 100d;
        return new RunProcessResourceMetrics(
            cpuDuration,
            corePercent,
            corePercent / logicalProcessorCount,
            Math.Max(0, peakWorkingSet),
            Math.Max(0, peakPrivate),
            Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - initialAllocatedBytes),
            Math.Max(0, GC.CollectionCount(0) - initialGen0),
            Math.Max(0, GC.CollectionCount(1) - initialGen1),
            Math.Max(0, GC.CollectionCount(2) - initialGen2),
            logicalProcessorCount);
    }

    public async ValueTask DisposeAsync()
    {
        cancellation.Cancel();
        if (samplingTask is not null)
        {
            try { await samplingTask.ConfigureAwait(false); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        }

        cancellation.Dispose();
    }

    private async Task SampleLoopAsync()
    {
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(1));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellation.Token).ConfigureAwait(false))
            {
                CapturePeak();
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
        }
    }

    private void CapturePeak()
    {
        ProcessResourceSample sample = source.Capture();
        InterlockedExtensions.Max(ref peakWorkingSet, sample.WorkingSetBytes);
        InterlockedExtensions.Max(ref peakPrivate, sample.PrivateBytes);
    }
}

internal static class InterlockedExtensions
{
    public static void Max(ref long target, long value)
    {
        long observed;
        while (value > (observed = Interlocked.Read(ref target))
            && Interlocked.CompareExchange(ref target, value, observed) != observed)
        {
        }
    }
}
