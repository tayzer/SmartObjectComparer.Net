namespace ParityBench.NET.Domain.Runs;

// Populated only when ObservabilityOptions.EnableDetailedCompareTiming is on: breaks the
// aggregate ComparisonDuration down by sub-step so a slow compare phase can be attributed
// to normalization, canonical artifact persistence, the actual diff, or focused-content
// building instead of one opaque number. Null when the toggle is off.
public sealed record CompareSubPhaseMetrics(
    TimeSpan NormalizeDuration,
    TimeSpan PersistCanonicalDuration,
    TimeSpan DiffDuration,
    TimeSpan FocusedContentDuration);

/// <summary>
/// Non-overlapping, aggregate-worker timings collected only when detailed compare
/// timing is enabled. A null value means the run predates this instrumentation or
/// it was intentionally disabled; it never means zero work occurred.
/// </summary>
public sealed record DetailedCompareMetrics(
    TimeSpan ArtifactOpenDuration,
    long ArtifactBytesRead,
    TimeSpan ResponseDeserializationDuration,
    TimeSpan ComparisonModelNormalizationDuration,
    TimeSpan CompareNetObjectsTraversalDuration,
    TimeSpan DifferenceMaterializationDuration,
    TimeSpan CanonicalMappingDuration,
    TimeSpan PluginMappingDuration,
    TimeSpan PluginPairProcessingDuration,
    TimeSpan FocusedContentDuration,
    TimeSpan OtherCompareWorkerDuration,
    TimeSpan CompareQueueWaitDuration,
    TimeSpan ExecutionWorkerBackpressureDuration);

/// <summary>Process-scoped resource evidence sampled while one run executes.</summary>
public sealed record RunProcessResourceMetrics(
    TimeSpan ProcessCpuDuration,
    double AverageProcessCoreUtilizationPercent,
    double AverageMachineCpuUtilizationPercent,
    long PeakWorkingSetBytes,
    long PeakPrivateBytes,
    long ManagedAllocatedBytes,
    int Gen0CollectionCount,
    int Gen1CollectionCount,
    int Gen2CollectionCount,
    int LogicalProcessorCount);

public sealed record RunExecutionMetrics
{
    public RunExecutionMetrics(
        TimeSpan totalDuration,
        TimeSpan requestExecutionDuration,
        TimeSpan comparisonDuration,
        TimeSpan finalizationDuration,
        int requestCount,
        int maxConcurrency,
        long responseBytesWritten,
        int retainedArtifactCount = 0,
        int trimmedByPolicyArtifactCount = 0,
        int missingUnexpectedlyArtifactCount = 0,
        CompareSubPhaseMetrics? compareSubPhases = null,
        int comparisonConcurrency = 0,
        DetailedCompareMetrics? detailedCompareMetrics = null,
        RunProcessResourceMetrics? processResourceMetrics = null)
    {
        EnsureNonNegative(totalDuration, nameof(totalDuration));
        EnsureNonNegative(requestExecutionDuration, nameof(requestExecutionDuration));
        EnsureNonNegative(comparisonDuration, nameof(comparisonDuration));
        EnsureNonNegative(finalizationDuration, nameof(finalizationDuration));
        EnsureNonNegative(requestCount, nameof(requestCount));
        EnsureNonNegative(maxConcurrency, nameof(maxConcurrency));
        EnsureNonNegative(responseBytesWritten, nameof(responseBytesWritten));
        EnsureNonNegative(comparisonConcurrency, nameof(comparisonConcurrency));
        EnsureNonNegative(retainedArtifactCount, nameof(retainedArtifactCount));
        EnsureNonNegative(trimmedByPolicyArtifactCount, nameof(trimmedByPolicyArtifactCount));
        EnsureNonNegative(missingUnexpectedlyArtifactCount, nameof(missingUnexpectedlyArtifactCount));

        TotalDuration = totalDuration;
        RequestExecutionDuration = requestExecutionDuration;
        ComparisonDuration = comparisonDuration;
        FinalizationDuration = finalizationDuration;
        RequestCount = requestCount;
        MaxConcurrency = maxConcurrency;
        ResponseBytesWritten = responseBytesWritten;
        ComparisonConcurrency = comparisonConcurrency;
        RetainedArtifactCount = retainedArtifactCount;
        TrimmedByPolicyArtifactCount = trimmedByPolicyArtifactCount;
        MissingUnexpectedlyArtifactCount = missingUnexpectedlyArtifactCount;
        CompareSubPhases = compareSubPhases;
        DetailedCompareMetrics = detailedCompareMetrics;
        ProcessResourceMetrics = processResourceMetrics;
    }

    public TimeSpan TotalDuration { get; }

    public TimeSpan RequestExecutionDuration { get; }

    public TimeSpan ComparisonDuration { get; }

    public TimeSpan FinalizationDuration { get; }

    public int RequestCount { get; }

    public int MaxConcurrency { get; }

    public long ResponseBytesWritten { get; }

    /// <summary>Actual number of workers used by the CPU-bound comparison stage.</summary>
    public int ComparisonConcurrency { get; }

    public int RetainedArtifactCount { get; }

    public int TrimmedByPolicyArtifactCount { get; }

    public int MissingUnexpectedlyArtifactCount { get; }

    public CompareSubPhaseMetrics? CompareSubPhases { get; }

    public DetailedCompareMetrics? DetailedCompareMetrics { get; }

    public RunProcessResourceMetrics? ProcessResourceMetrics { get; }

    private static void EnsureNonNegative(TimeSpan value, string parameterName)
    {
        if (value < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Duration values must not be negative.");
        }
    }

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Metric counts must not be negative.");
        }
    }

    private static void EnsureNonNegative(long value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Metric counts must not be negative.");
        }
    }
}
