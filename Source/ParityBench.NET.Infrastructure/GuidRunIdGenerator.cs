using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure;

public sealed class GuidRunIdGenerator : IRunIdGenerator
{
    public RunId CreateId() => new RunId($"run-{Guid.NewGuid():N}");
}
