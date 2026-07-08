namespace ParityBench.NET.Application.Observability;

public sealed class ObservabilityOptions
{
    public bool LogDurations { get; set; }

    public bool LogExceptions { get; set; } = true;

    public bool PersistDiagnostics { get; set; }

    public int SlowPathThresholdMs { get; set; } = 1000;

    public int MaxSlowPathEntries { get; set; } = 100;

    public int MaxExceptionEntries { get; set; } = 100;
}
