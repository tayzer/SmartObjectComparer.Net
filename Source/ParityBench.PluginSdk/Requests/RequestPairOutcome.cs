namespace ParityBench.NET.Domain.Requests;

public enum RequestPairOutcome
{
    Equal,
    Different,
    StatusCodeMismatch,
    BothNonSuccess,
    ExecutionFailed,
}
