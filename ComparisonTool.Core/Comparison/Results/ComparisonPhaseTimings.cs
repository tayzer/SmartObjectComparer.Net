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
    private long fileDiscoveryPairingMs;
    private long deserializationMs;
    private long xmlDeserializationPrecheckMs;
    private long xmlDeserializationFullDeserializeMs;
    private long compareMs;
    private long filterMs;
    private long collectionOrderDeterministicOrderingMs;
    private long collectionOrderFallbackMs;
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
        Interlocked.Add(ref fileDiscoveryPairingMs, ToMilliseconds(elapsed));
    }

    public void AddDeserialization(TimeSpan elapsed)
    {
        Interlocked.Add(ref deserializationMs, ToMilliseconds(elapsed));
    }

    public void AddXmlDeserializationPrecheck(TimeSpan elapsed)
    {
        Interlocked.Add(ref xmlDeserializationPrecheckMs, ToMilliseconds(elapsed));
    }

    public void AddXmlDeserializationFullDeserialize(TimeSpan elapsed)
    {
        Interlocked.Add(ref xmlDeserializationFullDeserializeMs, ToMilliseconds(elapsed));
    }

    public void AddComparison(TimeSpan elapsed)
    {
        AddCompare(elapsed);
    }

    public void AddCompare(TimeSpan elapsed)
    {
        Interlocked.Add(ref compareMs, ToMilliseconds(elapsed));
    }

    public void AddFilter(TimeSpan elapsed)
    {
        Interlocked.Add(ref filterMs, ToMilliseconds(elapsed));
    }

    public void AddCollectionOrderDeterministicOrdering(TimeSpan elapsed)
    {
        Interlocked.Add(ref collectionOrderDeterministicOrderingMs, ToMilliseconds(elapsed));
    }

    public void AddCollectionOrderFallback(TimeSpan elapsed)
    {
        Interlocked.Add(ref collectionOrderFallbackMs, ToMilliseconds(elapsed));
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
        FileDiscoveryPairingMs = Volatile.Read(ref fileDiscoveryPairingMs),
        DeserializationMs = Volatile.Read(ref deserializationMs),
        XmlDeserializationPrecheckMs = Volatile.Read(ref xmlDeserializationPrecheckMs),
        XmlDeserializationFullDeserializeMs = Volatile.Read(ref xmlDeserializationFullDeserializeMs),
        CompareMs = Volatile.Read(ref compareMs),
        FilterMs = Volatile.Read(ref filterMs),
        CollectionOrderDeterministicOrderingMs = Volatile.Read(ref collectionOrderDeterministicOrderingMs),
        CollectionOrderFallbackMs = Volatile.Read(ref collectionOrderFallbackMs),
        CollectionOrderFallbackCount = Volatile.Read(ref collectionOrderFallbackCount),
        TotalElapsedMs = stopwatch.ElapsedMilliseconds,
        CacheHits = Volatile.Read(ref cacheHits),
        CacheMisses = Volatile.Read(ref cacheMisses),
    };

    private static long ToMilliseconds(TimeSpan elapsed) =>
        (long)Math.Round(elapsed.TotalMilliseconds, MidpointRounding.AwayFromZero);
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