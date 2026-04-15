namespace ComparisonTool.Core.Comparison.Results;

using System;
using System.Diagnostics;
using System.Threading;

public sealed class ComparisonPhaseTimings
{
    public const string MetadataKey = "PhaseTimings";

    public string ComparisonMode { get; init; } = string.Empty;

    public int TotalPairsCompared { get; init; }

    public long FileDiscoveryPairingMs { get; init; }

    public long DeserializationMs { get; init; }

    public long XmlDeserializationPrecheckMs { get; init; }

    public long XmlDeserializationFullDeserializeMs { get; init; }

    public long CompareMs { get; init; }

    public long FilterMs { get; init; }

    public long CollectionOrderDeterministicOrderingMs { get; init; }

    public long CollectionOrderFallbackMs { get; init; }

    public int CollectionOrderFallbackCount { get; init; }

    public long ComparisonMs => CompareMs + FilterMs;

    public long TotalElapsedMs { get; init; }

    public int CacheHits { get; init; }

    public int CacheMisses { get; init; }
}

internal sealed class ComparisonPhaseTimingContext
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private long fileDiscoveryPairingTicks;
    private long deserializationTicks;
    private long xmlDeserializationPrecheckTicks;
    private long xmlDeserializationFullDeserializeTicks;
    private long compareTicks;
    private long filterTicks;
    private long collectionOrderDeterministicOrderingTicks;
    private long collectionOrderFallbackTicks;
    private int collectionOrderFallbackCount;
    private int totalPairsCompared;
    private int cacheHits;
    private int cacheMisses;

    public ComparisonPhaseTimingContext(string comparisonMode)
    {
        ComparisonMode = comparisonMode;
    }

    public string ComparisonMode { get; }

    public void SetTotalPairsCompared(int totalPairs)
    {
        Volatile.Write(ref totalPairsCompared, totalPairs);
    }

    public void AddFileDiscoveryPairing(TimeSpan elapsed)
    {
        Interlocked.Add(ref fileDiscoveryPairingTicks, elapsed.Ticks);
    }

    public void AddDeserialization(TimeSpan elapsed)
    {
        Interlocked.Add(ref deserializationTicks, elapsed.Ticks);
    }

    public void AddXmlDeserializationPrecheck(TimeSpan elapsed)
    {
        Interlocked.Add(ref xmlDeserializationPrecheckTicks, elapsed.Ticks);
    }

    public void AddXmlDeserializationFullDeserialize(TimeSpan elapsed)
    {
        Interlocked.Add(ref xmlDeserializationFullDeserializeTicks, elapsed.Ticks);
    }

    public void AddComparison(TimeSpan elapsed)
    {
        AddCompare(elapsed);
    }

    public void AddCompare(TimeSpan elapsed)
    {
        Interlocked.Add(ref compareTicks, elapsed.Ticks);
    }

    public void AddFilter(TimeSpan elapsed)
    {
        Interlocked.Add(ref filterTicks, elapsed.Ticks);
    }

    public void AddCollectionOrderDeterministicOrdering(TimeSpan elapsed)
    {
        Interlocked.Add(ref collectionOrderDeterministicOrderingTicks, elapsed.Ticks);
    }

    public void AddCollectionOrderFallback(TimeSpan elapsed)
    {
        Interlocked.Add(ref collectionOrderFallbackTicks, elapsed.Ticks);
        Interlocked.Increment(ref collectionOrderFallbackCount);
    }

    public void RecordCacheHit()
    {
        Interlocked.Increment(ref cacheHits);
    }

    public void RecordCacheMiss()
    {
        Interlocked.Increment(ref cacheMisses);
    }

    public ComparisonPhaseTimings CreateSnapshot() => new ()
    {
        ComparisonMode = ComparisonMode,
        TotalPairsCompared = Volatile.Read(ref totalPairsCompared),
        FileDiscoveryPairingMs = TicksToMilliseconds(Volatile.Read(ref fileDiscoveryPairingTicks)),
        DeserializationMs = TicksToMilliseconds(Volatile.Read(ref deserializationTicks)),
        XmlDeserializationPrecheckMs = TicksToMilliseconds(Volatile.Read(ref xmlDeserializationPrecheckTicks)),
        XmlDeserializationFullDeserializeMs = TicksToMilliseconds(Volatile.Read(ref xmlDeserializationFullDeserializeTicks)),
        CompareMs = TicksToMilliseconds(Volatile.Read(ref compareTicks)),
        FilterMs = TicksToMilliseconds(Volatile.Read(ref filterTicks)),
        CollectionOrderDeterministicOrderingMs = TicksToMilliseconds(Volatile.Read(ref collectionOrderDeterministicOrderingTicks)),
        CollectionOrderFallbackMs = TicksToMilliseconds(Volatile.Read(ref collectionOrderFallbackTicks)),
        CollectionOrderFallbackCount = Volatile.Read(ref collectionOrderFallbackCount),
        TotalElapsedMs = stopwatch.ElapsedMilliseconds,
        CacheHits = Volatile.Read(ref cacheHits),
        CacheMisses = Volatile.Read(ref cacheMisses),
    };

    private static long TicksToMilliseconds(long ticks) =>
        (long)Math.Round(TimeSpan.FromTicks(ticks).TotalMilliseconds, MidpointRounding.AwayFromZero);
}

internal static class ComparisonPhaseTimingScope
{
    private static readonly AsyncLocal<ComparisonPhaseTimingContext?> CurrentContext = new ();

    public static ComparisonPhaseTimingContext? Current => CurrentContext.Value;

    public static IDisposable Push(ComparisonPhaseTimingContext context) => new Scope(context);

    private sealed class Scope : IDisposable
    {
        private readonly ComparisonPhaseTimingContext? previousContext;

        public Scope(ComparisonPhaseTimingContext context)
        {
            previousContext = CurrentContext.Value;
            CurrentContext.Value = context;
        }

        public void Dispose()
        {
            CurrentContext.Value = previousContext;
        }
    }
}