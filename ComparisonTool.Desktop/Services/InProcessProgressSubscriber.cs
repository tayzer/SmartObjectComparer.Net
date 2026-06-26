using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// In-process progress subscriber that listens to the InProcessProgressPublisher events.
/// Replaces SignalR-based ComparisonProgressService for the desktop host.
/// </summary>
public class InProcessProgressSubscriber : IProgressSubscriber
{
    private readonly InProcessProgressPublisher _publisher;
    private readonly ILogger<InProcessProgressSubscriber> _logger;
    private string? _currentJobId;
    private readonly Dictionary<string, ComparisonProgressUpdate> _latestUpdatesByJob = new(StringComparer.Ordinal);
    private bool _started;
    private bool _disposed;

    /// <inheritdoc/>
    public event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    /// <inheritdoc/>
    public bool IsConnected => !_disposed;

    public InProcessProgressSubscriber(
        InProcessProgressPublisher publisher,
        ILogger<InProcessProgressSubscriber> logger)
    {
        _publisher = publisher;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task StartAsync()
    {
        if (_started)
        {
            return Task.CompletedTask;
        }

        _started = true;
        _publisher.OnProgressPublished += HandleProgressPublished;
        _logger.LogDebug("In-process progress subscriber started");
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SubscribeToJobAsync(string jobId)
    {
        _currentJobId = jobId;
        _logger.LogDebug("Subscribed to job {JobId}", jobId);

        if (_latestUpdatesByJob.TryGetValue(jobId, out var latestUpdate))
        {
            OnProgressUpdate?.Invoke(latestUpdate);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task UnsubscribeAsync()
    {
        _currentJobId = null;
        _logger.LogDebug("Unsubscribed from job updates");
        return Task.CompletedTask;
    }

    private void HandleProgressPublished(ComparisonProgressUpdate update)
    {
        _latestUpdatesByJob[update.JobId] = update;

        // Only forward updates for the subscribed job.
        if (_currentJobId != null && update.JobId == _currentJobId)
        {
            OnProgressUpdate?.Invoke(update);
        }
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            _started = false;
            _publisher.OnProgressPublished -= HandleProgressPublished;
        }

        return ValueTask.CompletedTask;
    }
}
