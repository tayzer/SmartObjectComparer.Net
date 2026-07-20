using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Domain.Requests;

public sealed record RequestPairResult
{
    public RequestPairResult(
        string relativePath,
        RequestPairOutcome outcome,
        ResponseArtifactMetadata? responseA = null,
        ResponseArtifactMetadata? responseB = null,
        string? errorMessage = null,
        bool? areEqual = null,
        int? differenceCount = null,
        IEnumerable<ComparisonDifference>? differences = null,
        string? outcomeMessage = null,
        IEnumerable<StaticReportRawTextDifference>? rawTextDifferences = null,
        ResponseArtifactMetadata? focusedResponseA = null,
        ResponseArtifactMetadata? focusedResponseB = null,
        IEnumerable<string>? focusedRawContentIgnorePaths = null,
        PairRetentionClass? pairRetentionClass = null,
        PairArtifactRetentionState? artifactRetentionState = null,
        DateTimeOffset? retentionAppliedAt = null)
    {
        RelativePath = new RequestItem(relativePath).RelativePath;
        Outcome = outcome;
        ResponseA = responseA;
        ResponseB = responseB;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
        OutcomeMessage = string.IsNullOrWhiteSpace(outcomeMessage) ? null : outcomeMessage;
        Differences = (differences ?? Array.Empty<ComparisonDifference>()).ToList();
        RawTextDifferences = (rawTextDifferences ?? Array.Empty<StaticReportRawTextDifference>()).ToList();
        FocusedResponseA = focusedResponseA;
        FocusedResponseB = focusedResponseB;
        PairRetentionClass = pairRetentionClass ?? MapPairRetentionClass(outcome);
        ArtifactRetentionState = artifactRetentionState ?? PairArtifactRetentionState.CreateDefaultRetained();
        RetentionAppliedAt = retentionAppliedAt;
        FocusedRawContentIgnorePaths = (focusedRawContentIgnorePaths ?? Array.Empty<string>())
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => path.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        DifferenceCount = differenceCount ?? Differences.Count;
        AreEqual = areEqual ?? (outcome switch
        {
            RequestPairOutcome.Equal => true,
            RequestPairOutcome.Different => false,
            _ => null
        });
    }

    public string RelativePath { get; }

    public RequestPairOutcome Outcome { get; }

    public ResponseArtifactMetadata? ResponseA { get; }

    public ResponseArtifactMetadata? ResponseB { get; }

    public string? ErrorMessage { get; }

    public string? OutcomeMessage { get; }

    public bool? AreEqual { get; }

    public int DifferenceCount { get; }

    public IReadOnlyList<ComparisonDifference> Differences { get; }

    public IReadOnlyList<StaticReportRawTextDifference> RawTextDifferences { get; }

    public ResponseArtifactMetadata? FocusedResponseA { get; }

    public ResponseArtifactMetadata? FocusedResponseB { get; }

    public IReadOnlyList<string> FocusedRawContentIgnorePaths { get; }

    public PairRetentionClass PairRetentionClass { get; }

    public PairArtifactRetentionState ArtifactRetentionState { get; }

    public DateTimeOffset? RetentionAppliedAt { get; }

    public bool HasFocusedRawContent =>
        FocusedRawContentIgnorePaths.Count > 0 &&
        (FocusedResponseA?.Artifact is not null || FocusedResponseB?.Artifact is not null);

    public RequestPairResult WithFocusedRawContent(
        ResponseArtifactMetadata focusedResponseA,
        ResponseArtifactMetadata focusedResponseB,
        IEnumerable<string> focusedRawContentIgnorePaths) =>
        new RequestPairResult(
            RelativePath,
            Outcome,
            ResponseA,
            ResponseB,
            ErrorMessage,
            AreEqual,
            DifferenceCount,
            Differences,
            OutcomeMessage,
            RawTextDifferences,
            focusedResponseA,
            focusedResponseB,
            focusedRawContentIgnorePaths,
            PairRetentionClass,
            ArtifactRetentionState,
            RetentionAppliedAt);

    public RequestPairResult WithRetention(
        PairArtifactRetentionState artifactRetentionState,
        DateTimeOffset retentionAppliedAt,
        PairRetentionClass? pairRetentionClass = null) =>
        new RequestPairResult(
            RelativePath,
            Outcome,
            ResponseA,
            ResponseB,
            ErrorMessage,
            AreEqual,
            DifferenceCount,
            Differences,
            OutcomeMessage,
            RawTextDifferences,
            FocusedResponseA,
            FocusedResponseB,
            FocusedRawContentIgnorePaths,
            pairRetentionClass ?? PairRetentionClass,
            artifactRetentionState,
            retentionAppliedAt);

    public static RequestPairResult Classify(
        RequestItem request,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage = null)
    {
        if (!string.IsNullOrWhiteSpace(errorMessage) || responseA is null || responseB is null)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.ExecutionFailed,
                responseA,
                responseB,
                errorMessage);
        }

        bool endpointASucceeded = IsSuccessStatusCode(responseA.StatusCode);
        bool endpointBSucceeded = IsSuccessStatusCode(responseB.StatusCode);

        if (endpointASucceeded && endpointBSucceeded)
        {
            RequestPairOutcome outcome = responseA.ContentLength == responseB.ContentLength
                && string.Equals(responseA.Sha256, responseB.Sha256, StringComparison.OrdinalIgnoreCase)
                    ? RequestPairOutcome.Equal
                    : RequestPairOutcome.Different;

            return new RequestPairResult(
                request.RelativePath,
                outcome,
                responseA,
                responseB,
                areEqual: outcome == RequestPairOutcome.Equal,
                differenceCount: outcome == RequestPairOutcome.Equal ? 0 : 1);
        }

        if (endpointASucceeded != endpointBSucceeded)
        {
            return new RequestPairResult(
                request.RelativePath,
                RequestPairOutcome.StatusCodeMismatch,
                responseA,
                responseB,
                outcomeMessage: BuildStatusOutcomeMessage(RequestPairOutcome.StatusCodeMismatch, responseA.StatusCode, responseB.StatusCode));
        }

        return new RequestPairResult(
            request.RelativePath,
            RequestPairOutcome.BothNonSuccess,
            responseA,
            responseB,
            outcomeMessage: BuildStatusOutcomeMessage(RequestPairOutcome.BothNonSuccess, responseA.StatusCode, responseB.StatusCode));
    }

    public static RequestPairResult FromComparison(
        RequestItem request,
        ResponseArtifactMetadata responseA,
        ResponseArtifactMetadata responseB,
        IEnumerable<ComparisonDifference> differences,
        string? equalitySummary = null)
    {
        List<ComparisonDifference> materializedDifferences = differences.ToList();
        bool areEqual = materializedDifferences.Count == 0;

        return new RequestPairResult(
            request.RelativePath,
            areEqual ? RequestPairOutcome.Equal : RequestPairOutcome.Different,
            responseA,
            responseB,
            areEqual: areEqual,
            differenceCount: materializedDifferences.Count,
            differences: materializedDifferences,
            outcomeMessage: equalitySummary);
    }

    public static RequestPairResult FromRawTextComparison(
        RequestItem request,
        ResponseArtifactMetadata responseA,
        ResponseArtifactMetadata responseB,
        IEnumerable<ComparisonDifference> differences,
        string? outcomeMessage = null)
    {
        bool endpointASucceeded = IsSuccessStatusCode(responseA.StatusCode);
        bool endpointBSucceeded = IsSuccessStatusCode(responseB.StatusCode);
        if (endpointASucceeded && endpointBSucceeded)
        {
            throw new InvalidOperationException("Raw text comparison is only valid for non-success response pairs.");
        }

        List<ComparisonDifference> materializedDifferences = differences.ToList();
        RequestPairOutcome outcome = endpointASucceeded != endpointBSucceeded
            ? RequestPairOutcome.StatusCodeMismatch
            : RequestPairOutcome.BothNonSuccess;

        return new RequestPairResult(
            request.RelativePath,
            outcome,
            responseA,
            responseB,
            areEqual: null,
            differenceCount: materializedDifferences.Count,
            differences: materializedDifferences,
            outcomeMessage: outcomeMessage ?? BuildStatusOutcomeMessage(outcome, responseA.StatusCode, responseB.StatusCode));
    }

    public static RunResultSummary Summarize(
        IEnumerable<RequestPairResult> results,
        RunDetailReference? detailReference = null,
        RunExecutionMetrics? executionMetrics = null)
    {
        List<RequestPairResult> materializedResults = results.ToList();

        return new RunResultSummary(
            materializedResults.Count,
            materializedResults.Count(result => result.Outcome == RequestPairOutcome.Equal),
            materializedResults.Count(result => result.Outcome == RequestPairOutcome.Different),
            materializedResults.Count(result => result.Outcome == RequestPairOutcome.ExecutionFailed),
            materializedResults.Count(result => result.Outcome == RequestPairOutcome.StatusCodeMismatch),
            materializedResults.Count(result => result.Outcome == RequestPairOutcome.BothNonSuccess),
            detailReference,
            executionMetrics);
    }

    private static bool IsSuccessStatusCode(int statusCode) => statusCode is >= 200 and <= 299;

    private static PairRetentionClass MapPairRetentionClass(RequestPairOutcome outcome) => outcome switch
    {
        RequestPairOutcome.Equal => PairRetentionClass.Equal,
        RequestPairOutcome.Different => PairRetentionClass.Different,
        RequestPairOutcome.ExecutionFailed => PairRetentionClass.ExecutionFailed,
        RequestPairOutcome.StatusCodeMismatch => PairRetentionClass.StatusCodeMismatch,
        RequestPairOutcome.BothNonSuccess => PairRetentionClass.BothNonSuccess,
        _ => PairRetentionClass.ExecutionFailed,
    };

    private static string BuildStatusOutcomeMessage(
        RequestPairOutcome outcome,
        int statusCodeA,
        int statusCodeB) =>
        outcome == RequestPairOutcome.StatusCodeMismatch
            ? $"Endpoint A returned {statusCodeA} and endpoint B returned {statusCodeB}."
            : $"Both endpoints returned non-success status codes: A={statusCodeA}, B={statusCodeB}.";
}

