using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Reports executor progress to the Application lifecycle service.
/// </summary>
public interface IRunProgressReporter
{
    /// <summary>
    /// Reports the latest lifecycle status and progress for the active run.
    /// </summary>
    Task ReportAsync(
        RunStatus status,
        RunProgress progress,
        CancellationToken cancellationToken = default);

    Task ReportAsync(
        RunStatus status,
        RunProgress progress,
        CancellationToken cancellationToken = default,
        bool force = false) =>
        ReportAsync(status, progress, cancellationToken);
}
