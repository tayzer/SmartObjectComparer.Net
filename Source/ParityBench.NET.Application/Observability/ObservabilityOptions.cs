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

    // Opt-in and privacy-safe: exports only salted identifier hashes and aggregate
    // structural distributions. No request or response content is written.
    public bool EnableStructuralFingerprintExport { get; set; }

    // Defaults to the operating-system temporary directory when blank.
    public string? StructuralFingerprintOutputDirectory { get; set; }

    // Explicit private-data opt-in. The next run's first 1,000 raw response pairs
    // are copied before retention so the client machine can perform offline replay.
    public bool CaptureNextRunForCalibration { get; set; }

    // Defaults to the current user's local application-data calibration directory.
    public string? CalibrationCaptureOutputDirectory { get; set; }
}
