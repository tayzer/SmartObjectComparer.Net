using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Creates unique identifiers for new comparison runs.
/// </summary>
public interface IRunIdGenerator
{
    /// <summary>
    /// Creates the next run identifier.
    /// </summary>
    RunId CreateId();
}
