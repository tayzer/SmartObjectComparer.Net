using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic progress subscription service.
/// Web implementation uses SignalR HubConnection.
/// Desktop implementation uses in-process events.
/// </summary>
public interface IProgressSubscriber : IAsyncDisposable
{
    /// <summary>
    /// Event raised when a progress update is received.
    /// </summary>
    event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    /// <summary>
    /// Whether the subscriber is currently connected/active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Starts listening for progress updates.
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Subscribes to updates for a specific job.
    /// </summary>
    Task SubscribeToJobAsync(string jobId);

    /// <summary>
    /// Unsubscribes from the current job.
    /// </summary>
    Task UnsubscribeAsync();
}
