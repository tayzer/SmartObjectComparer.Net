namespace ParityBench.NET.Domain.Runs;

public sealed record RunExecutionMetrics
{
    public RunExecutionMetrics(
        TimeSpan totalDuration,
        TimeSpan requestExecutionDuration,
        TimeSpan comparisonDuration,
        TimeSpan finalizationDuration,
        int requestCount,
        int maxConcurrency,
        long responseBytesWritten)
    {
        EnsureNonNegative(totalDuration, nameof(totalDuration));
        EnsureNonNegative(requestExecutionDuration, nameof(requestExecutionDuration));
        EnsureNonNegative(comparisonDuration, nameof(comparisonDuration));
        EnsureNonNegative(finalizationDuration, nameof(finalizationDuration));
        EnsureNonNegative(requestCount, nameof(requestCount));
        EnsureNonNegative(maxConcurrency, nameof(maxConcurrency));
        EnsureNonNegative(responseBytesWritten, nameof(responseBytesWritten));

        TotalDuration = totalDuration;
        RequestExecutionDuration = requestExecutionDuration;
        ComparisonDuration = comparisonDuration;
        FinalizationDuration = finalizationDuration;
        RequestCount = requestCount;
        MaxConcurrency = maxConcurrency;
        ResponseBytesWritten = responseBytesWritten;
    }

    public TimeSpan TotalDuration { get; }

    public TimeSpan RequestExecutionDuration { get; }

    public TimeSpan ComparisonDuration { get; }

    public TimeSpan FinalizationDuration { get; }

    public int RequestCount { get; }

    public int MaxConcurrency { get; }

    public long ResponseBytesWritten { get; }

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
