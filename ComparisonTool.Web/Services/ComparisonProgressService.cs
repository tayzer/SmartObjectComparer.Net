using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Client-side service for receiving comparison progress updates via SignalR.
/// </summary>
public class ComparisonProgressService : IAsyncDisposable
{
    private HubConnection? hubConnection;
    private readonly NavigationManager navigationManager;
    private readonly ILogger<ComparisonProgressService> logger;
    private string? currentJobId;
    private bool disposed;

    /// <summary>
    /// Event raised when a progress update is received.
    /// </summary>
    public event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    /// <summary>
    /// Gets a value indicating whether the service is connected.
    /// </summary>
    public bool IsConnected => hubConnection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComparisonProgressService"/> class.
    /// </summary>
    public ComparisonProgressService(
        NavigationManager navigationManager,
        ILogger<ComparisonProgressService> logger)
    {
        this.navigationManager = navigationManager;
        this.logger = logger;
    }

    /// <summary>
    /// Starts the SignalR connection.
    /// </summary>
    public async Task StartAsync()
    {
        // Build the hub connection once, and reuse it across restarts.
        if (hubConnection == null)
        {
            hubConnection = new HubConnectionBuilder()
                .WithUrl(navigationManager.ToAbsoluteUri("/hubs/comparison-progress"))
                .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
                .Build();

            hubConnection.On<ComparisonProgressUpdate>("ProgressUpdate", update =>
            {
                logger.LogTrace("Received progress update for job {JobId}: {Phase} {Percent}%", 
                    update.JobId, update.Phase, update.PercentComplete);
                OnProgressUpdate?.Invoke(update);
            });

            hubConnection.Reconnecting += error =>
            {
                logger.LogWarning(error, "SignalR connection lost, attempting to reconnect...");
                return Task.CompletedTask;
            };

            hubConnection.Reconnected += async connectionId =>
            {
                logger.LogInformation("SignalR reconnected with connection ID: {ConnectionId}", connectionId);
                if (!string.IsNullOrEmpty(currentJobId))
                {
                    try
                    {
                        await hubConnection!.InvokeAsync("SubscribeToJob", currentJobId);
                        logger.LogDebug("Re-subscribed to job {JobId} after reconnection", currentJobId);
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to re-subscribe to job {JobId} after reconnection", currentJobId);
                    }
                }
            };
        }

        // If already connected, nothing to do.
        if (hubConnection.State == HubConnectionState.Connected)
        {
            return;
        }

        // Avoid calling StartAsync when the connection is in an intermediate state
        // (Connecting/Reconnecting), which would throw InvalidOperationException.
        if (hubConnection.State == HubConnectionState.Connecting ||
            hubConnection.State == HubConnectionState.Reconnecting)
        {
            logger.LogDebug("SignalR connection is currently {State}; skipping StartAsync.", hubConnection.State);
            return;
        }

        try
        {
            await hubConnection.StartAsync();
            logger.LogDebug("SignalR connection started");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start SignalR connection");
            throw;
        }
    }

    /// <summary>
    /// Subscribes to progress updates for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to.</param>
    public async Task SubscribeToJobAsync(string jobId)
    {
        if (hubConnection == null || hubConnection.State != HubConnectionState.Connected)
        {
            await StartAsync();
        }

        if (!string.IsNullOrEmpty(currentJobId) && currentJobId != jobId)
        {
            try
            {
                await hubConnection!.InvokeAsync("UnsubscribeFromJob", currentJobId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to unsubscribe from job {JobId}", currentJobId);
            }
        }

        currentJobId = jobId;

        try
        {
            await hubConnection!.InvokeAsync("SubscribeToJob", jobId);
            logger.LogDebug("Subscribed to job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to subscribe to job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Unsubscribes from the current job.
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        if (hubConnection != null && !string.IsNullOrEmpty(currentJobId))
        {
            try
            {
                await hubConnection.InvokeAsync("UnsubscribeFromJob", currentJobId);
                logger.LogDebug("Unsubscribed from job {JobId}", currentJobId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to unsubscribe from job {JobId}", currentJobId);
            }
            finally
            {
                currentJobId = null;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        if (hubConnection != null)
        {
            try
            {
                await hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error disposing SignalR connection");
            }
        }
    }
}
