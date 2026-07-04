namespace ParityBench.NET.Domain.Runs;

public enum RunStatus
{
    Created,
    Pending,
    Parsing,
    Executing,
    Comparing,
    Analyzing,
    Finalizing,
    Completed,
    Failed,
    Cancelled,
}
