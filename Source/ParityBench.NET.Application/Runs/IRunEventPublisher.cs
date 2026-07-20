using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Publishes comparison-run lifecycle events to host or adapter boundaries.
/// </summary>
public interface IRunEventPublisher
{
    /// <summary>
    /// Publishes a run lifecycle or progress event.
    /// </summary>
    Task PublishAsync(RunEvent runEvent, CancellationToken cancellationToken = default);
}
