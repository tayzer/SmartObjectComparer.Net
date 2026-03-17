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

    public long ComparisonMs { get; init; }

    public long TotalElapsedMs { get; init; }

    public int CacheHits { get; init; }

    public int CacheMisses { get; init; }
}

internal sealed class ComparisonPhaseTimingContext
{
    private readonly Stopwatch stopwatch = Stopwatch.StartNew();
    private long fileDiscoveryPairingMs;
    private long deserializationMs;
    private long comparisonMs;
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

    public void AddComparison(TimeSpan elapsed)
    {
        Interlocked.Add(ref comparisonMs, ToMilliseconds(elapsed));
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
        ComparisonMs = Volatile.Read(ref comparisonMs),
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