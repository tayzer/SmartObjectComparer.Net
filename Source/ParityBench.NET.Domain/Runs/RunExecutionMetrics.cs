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
        int comparisonConcurrency = 0)
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
