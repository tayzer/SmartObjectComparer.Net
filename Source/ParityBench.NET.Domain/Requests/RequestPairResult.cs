using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Requests;

public sealed record RequestPairResult
{
    public RequestPairResult(
        string relativePath,
        RequestPairOutcome outcome,
        ResponseArtifactMetadata? responseA = null,
        ResponseArtifactMetadata? responseB = null,
        string? errorMessage = null)
    {
        RelativePath = new RequestItem(relativePath).RelativePath;
        Outcome = outcome;
        ResponseA = responseA;
        ResponseB = responseB;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
    }

    public string RelativePath { get; }

    public RequestPairOutcome Outcome { get; }

    public ResponseArtifactMetadata? ResponseA { get; }

    public ResponseArtifactMetadata? ResponseB { get; }

    public string? ErrorMessage { get; }

    public static RequestPairResult Classify(
        RequestItem request,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        RequestPairOutcome outcome = ClassifyOutcome(responseA, responseB, errorMessage);
        return new RequestPairResult(request.RelativePath, outcome, responseA, responseB, errorMessage);
    }

    public static RunResultSummary Summarize(
        IEnumerable<RequestPairResult> results,
        RunDetailReference? detailIndexReference = null)
    {
        ArgumentNullException.ThrowIfNull(results);

        List<RequestPairResult> materializedResults = results.ToList();
        return new RunResultSummary(
            totalPairs: materializedResults.Count,
            equalPairs: materializedResults.Count(result => result.Outcome == RequestPairOutcome.Equal),
            differentPairs: materializedResults.Count(result => result.Outcome == RequestPairOutcome.Different),
            errorPairs: materializedResults.Count(result => result.Outcome == RequestPairOutcome.ExecutionFailed),
            statusCodeMismatchPairs: materializedResults.Count(result => result.Outcome == RequestPairOutcome.StatusCodeMismatch),
            bothNonSuccessPairs: materializedResults.Count(result => result.Outcome == RequestPairOutcome.BothNonSuccess),
            detailIndexReference: detailIndexReference);
    }

    private static RequestPairOutcome ClassifyOutcome(
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB,
        string? errorMessage)
    {
        if (responseA is null || responseB is null || !string.IsNullOrWhiteSpace(errorMessage))
        {
            return RequestPairOutcome.ExecutionFailed;
        }

        bool aSuccess = IsSuccess(responseA.StatusCode);
        bool bSuccess = IsSuccess(responseB.StatusCode);
        if (aSuccess != bSuccess)
        {
            return RequestPairOutcome.StatusCodeMismatch;
        }

        if (!aSuccess && !bSuccess)
        {
            return RequestPairOutcome.BothNonSuccess;
        }

        return responseA.ContentLength == responseB.ContentLength
            && string.Equals(responseA.Sha256, responseB.Sha256, StringComparison.OrdinalIgnoreCase)
                ? RequestPairOutcome.Equal
                : RequestPairOutcome.Different;
    }

    private static bool IsSuccess(int statusCode) => statusCode is >= 200 and <= 299;
}
