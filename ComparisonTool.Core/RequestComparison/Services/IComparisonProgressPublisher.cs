using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Interface for publishing comparison progress updates.
/// </summary>
public interface IComparisonProgressPublisher
{
    /// <summary>
    /// Publishes a progress update for a comparison job.
    /// </summary>
    /// <param name="update">The progress update to publish.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default);
}
