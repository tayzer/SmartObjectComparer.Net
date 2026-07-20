namespace ParityBench.NET.Domain.Runs.Retention;

public enum PairRetentionClass
{
    Equal,
    Different,
    ExecutionFailed,
    StatusCodeMismatch,
    BothNonSuccess,
}