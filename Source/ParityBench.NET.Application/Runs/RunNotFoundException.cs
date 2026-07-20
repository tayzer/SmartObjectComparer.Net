using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

public sealed class RunNotFoundException : KeyNotFoundException
{
    public RunNotFoundException(RunId runId)
        : base($"Run '{runId}' was not found.")
    {
        RunId = runId;
    }

    public RunId RunId { get; }
}
