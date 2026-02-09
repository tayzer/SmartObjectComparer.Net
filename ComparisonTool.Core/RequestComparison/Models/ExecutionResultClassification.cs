namespace ComparisonTool.Core.RequestComparison.Models;

/// <summary>
/// Classifies the HTTP outcome of an A/B request pair.
/// </summary>
public enum RequestPairOutcome
{
    /// <summary>Both endpoints returned 2xx — normal domain-model comparison.</summary>
    BothSuccess,

    /// <summary>One returned 2xx, the other did not — critical mismatch.</summary>
    StatusCodeMismatch,

    /// <summary>Both returned non-2xx — compare error bodies as raw text.</summary>
    BothNonSuccess,

    /// <summary>One or both threw exceptions (timeout, DNS, etc.).</summary>
    OneOrBothFailed,
}

/// <summary>
/// Wraps an execution result with its classified outcome.
/// </summary>
public record ClassifiedExecutionResult
{
    /// <summary>Gets the underlying execution result.</summary>
    public required RequestExecutionResult Execution { get; init; }

    /// <summary>Gets the classified pair outcome.</summary>
    public required RequestPairOutcome Outcome { get; init; }

    /// <summary>Gets a human-readable reason for the classification (e.g. "A=200, B=500").</summary>
    public string? OutcomeReason { get; init; }
}

/// <summary>
/// Structured summary of execution outcomes for a request comparison job.
/// </summary>
public record ExecutionOutcomeSummary
{
    /// <summary>Gets the total number of requests executed.</summary>
    public int TotalRequests { get; init; }

    /// <summary>Gets the number of pairs where both endpoints returned 2xx.</summary>
    public int BothSuccess { get; init; }

    /// <summary>Gets the number of pairs where status codes differed (one 2xx, one non-2xx).</summary>
    public int StatusCodeMismatch { get; init; }

    /// <summary>Gets the number of pairs where both endpoints returned non-2xx.</summary>
    public int BothNonSuccess { get; init; }

    /// <summary>Gets the number of pairs where one or both requests failed with exceptions.</summary>
    public int OneOrBothFailed { get; init; }
}

/// <summary>
/// Helper for classifying execution results by HTTP outcome.
/// </summary>
public static class ExecutionResultClassifier
{
    /// <summary>
    /// Classifies a single execution result based on the HTTP status codes returned.
    /// </summary>
    public static RequestPairOutcome Classify(RequestExecutionResult result)
    {
        if (!result.Success)
        {
            return RequestPairOutcome.OneOrBothFailed;
        }

        var aOk = result.StatusCodeA is >= 200 and < 300;
        var bOk = result.StatusCodeB is >= 200 and < 300;

        return (aOk, bOk) switch
        {
            (true, true) => RequestPairOutcome.BothSuccess,
            (false, false) => RequestPairOutcome.BothNonSuccess,
            _ => RequestPairOutcome.StatusCodeMismatch,
        };
    }

    /// <summary>
    /// Classifies a list of execution results and wraps them with outcome metadata.
    /// </summary>
    public static IReadOnlyList<ClassifiedExecutionResult> ClassifyAll(
        IEnumerable<RequestExecutionResult> results)
    {
        return results.Select(r => new ClassifiedExecutionResult
        {
            Execution = r,
            Outcome = Classify(r),
            OutcomeReason = r.Success
                ? $"A={r.StatusCodeA}, B={r.StatusCodeB}"
                : $"Failed: {r.ErrorMessage}",
        }).ToList();
    }

    /// <summary>
    /// Creates a structured summary of outcomes from classified results.
    /// </summary>
    public static ExecutionOutcomeSummary Summarize(IReadOnlyList<ClassifiedExecutionResult> classified)
    {
        return new ExecutionOutcomeSummary
        {
            TotalRequests = classified.Count,
            BothSuccess = classified.Count(c => c.Outcome == RequestPairOutcome.BothSuccess),
            StatusCodeMismatch = classified.Count(c => c.Outcome == RequestPairOutcome.StatusCodeMismatch),
            BothNonSuccess = classified.Count(c => c.Outcome == RequestPairOutcome.BothNonSuccess),
            OneOrBothFailed = classified.Count(c => c.Outcome == RequestPairOutcome.OneOrBothFailed),
        };
    }
}
