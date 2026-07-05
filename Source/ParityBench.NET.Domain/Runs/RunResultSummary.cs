namespace ParityBench.NET.Domain.Runs;

public sealed record RunResultSummary
{
    public RunResultSummary(
        int totalPairs,
        int equalPairs,
        int differentPairs,
        int errorPairs,
        int statusCodeMismatchPairs = 0,
        int bothNonSuccessPairs = 0,
        RunDetailReference? detailIndexReference = null,
        RunExecutionMetrics? executionMetrics = null)
    {
        EnsureNonNegative(totalPairs, nameof(totalPairs));
        EnsureNonNegative(equalPairs, nameof(equalPairs));
        EnsureNonNegative(differentPairs, nameof(differentPairs));
        EnsureNonNegative(errorPairs, nameof(errorPairs));
        EnsureNonNegative(statusCodeMismatchPairs, nameof(statusCodeMismatchPairs));
        EnsureNonNegative(bothNonSuccessPairs, nameof(bothNonSuccessPairs));

        TotalPairs = totalPairs;
        EqualPairs = equalPairs;
        DifferentPairs = differentPairs;
        ErrorPairs = errorPairs;
        StatusCodeMismatchPairs = statusCodeMismatchPairs;
        BothNonSuccessPairs = bothNonSuccessPairs;
        DetailIndexReference = detailIndexReference;
        ExecutionMetrics = executionMetrics;
    }

    public int TotalPairs { get; }

    public int EqualPairs { get; }

    public int DifferentPairs { get; }

    public int ErrorPairs { get; }

    public int StatusCodeMismatchPairs { get; }

    public int BothNonSuccessPairs { get; }

    public RunDetailReference? DetailIndexReference { get; }

    public RunExecutionMetrics? ExecutionMetrics { get; }

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Summary counts must not be negative.");
        }
    }
}
