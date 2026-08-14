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

internal sealed class CompareSubPhaseCounters
{
    public CompareSubPhaseCounters(bool collectStructuralFingerprint = false)
    {
        Detailed = new DetailedCompareMetricsCollector(collectStructuralFingerprint);
    }

    public DetailedCompareMetricsCollector Detailed { get; }
    private long normalizeTicks;
    private long persistCanonicalTicks;
    private long diffTicks;
    private long focusedContentTicks;

    public void AddNormalize(TimeSpan elapsed) => Interlocked.Add(ref normalizeTicks, elapsed.Ticks);

    public void AddPersistCanonical(TimeSpan elapsed) => Interlocked.Add(ref persistCanonicalTicks, elapsed.Ticks);

    public void AddDiff(TimeSpan elapsed) => Interlocked.Add(ref diffTicks, elapsed.Ticks);

    public void AddFocusedContent(TimeSpan elapsed) => Interlocked.Add(ref focusedContentTicks, elapsed.Ticks);

    public CompareSubPhaseMetrics ToMetrics() => new CompareSubPhaseMetrics(
        TimeSpan.FromTicks(Interlocked.Read(ref normalizeTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref persistCanonicalTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref diffTicks)),
        TimeSpan.FromTicks(Interlocked.Read(ref focusedContentTicks)));
}
