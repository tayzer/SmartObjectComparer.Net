using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// In-process progress publisher that raises events instead of sending SignalR messages.
/// Desktop subscribers listen to the event directly.
/// </summary>
public class InProcessProgressPublisher : IComparisonProgressPublisher
{
    private readonly ILogger<InProcessProgressPublisher> _logger;

    /// <summary>
    /// Event raised when a progress update is published.
    /// </summary>
    public event Action<ComparisonProgressUpdate>? OnProgressPublished;

    public InProcessProgressPublisher(ILogger<InProcessProgressPublisher> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        _logger.LogTrace(
            "Publishing progress for job {JobId}: {Phase} {Percent}% - {Message}",
            update.JobId,
            update.Phase,
            update.PercentComplete,
            update.Message);

        OnProgressPublished?.Invoke(update);

        return Task.CompletedTask;
    }
}
