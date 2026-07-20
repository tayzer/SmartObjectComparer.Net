using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Executes a comparison run behind the Application lifecycle boundary.
/// </summary>
public interface IComparisonRunExecutor
{
    /// <summary>
    /// Executes the run and reports lifecycle progress back to the Application layer.
    /// </summary>
    Task<RunResultSummary> ExecuteAsync(
        ComparisonRun run,
        IRunProgressReporter progressReporter,
        CancellationToken cancellationToken = default);
}
