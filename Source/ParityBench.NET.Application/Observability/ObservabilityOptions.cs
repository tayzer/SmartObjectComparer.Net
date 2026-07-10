namespace ParityBench.NET.Application.Observability;

public sealed class ObservabilityOptions
{
    public bool LogDurations { get; set; }

    public bool LogExceptions { get; set; } = true;

    public bool PersistDiagnostics { get; set; }

    public int SlowPathThresholdMs { get; set; } = 1000;

    public int MaxSlowPathEntries { get; set; } = 100;

    public int MaxExceptionEntries { get; set; } = 100;

    // Off by default: breaks the compare-phase duration down by sub-step (normalize,
    // persist canonical artifacts, diff, focused-content build) for diagnosing slow
    // comparisons. Adds a few Stopwatch calls per request; switch off once the cause
    // of a slowdown is found and the run is healthy again.
    public bool EnableDetailedCompareTiming { get; set; }
}
