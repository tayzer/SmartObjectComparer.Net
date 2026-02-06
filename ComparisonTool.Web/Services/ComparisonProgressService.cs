using ComparisonTool.Core.RequestComparison.Models;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.SignalR.Client;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Client-side service for receiving comparison progress updates via SignalR.
/// </summary>
public class ComparisonProgressService : IAsyncDisposable
{
    private HubConnection? _hubConnection;
    private readonly NavigationManager _navigationManager;
    private readonly ILogger<ComparisonProgressService> _logger;
    private string? _currentJobId;
    private bool _disposed;

    /// <summary>
    /// Event raised when a progress update is received.
    /// </summary>
    public event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    /// <summary>
    /// Gets a value indicating whether the service is connected.
    /// </summary>
    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComparisonProgressService"/> class.
    /// </summary>
    public ComparisonProgressService(
        NavigationManager navigationManager,
        ILogger<ComparisonProgressService> logger)
    {
        _navigationManager = navigationManager;
        _logger = logger;
    }

    /// <summary>
    /// Starts the SignalR connection.
    /// </summary>
    public async Task StartAsync()
    {
        if (_hubConnection != null)
        {
            return;
        }

        _hubConnection = new HubConnectionBuilder()
            .WithUrl(_navigationManager.ToAbsoluteUri("/hubs/comparison-progress"))
            .WithAutomaticReconnect(new[] { TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) })
            .Build();

        _hubConnection.On<ComparisonProgressUpdate>("ProgressUpdate", update =>
        {
            _logger.LogTrace("Received progress update for job {JobId}: {Phase} {Percent}%", 
                update.JobId, update.Phase, update.PercentComplete);
            OnProgressUpdate?.Invoke(update);
        });

        _hubConnection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "SignalR connection lost, attempting to reconnect...");
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += connectionId =>
        {
            _logger.LogInformation("SignalR reconnected with connection ID: {ConnectionId}", connectionId);
            if (!string.IsNullOrEmpty(_currentJobId))
            {
                _ = _hubConnection.InvokeAsync("SubscribeToJob", _currentJobId);
            }
            return Task.CompletedTask;
        };

        try
        {
            await _hubConnection.StartAsync();
            _logger.LogDebug("SignalR connection started");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start SignalR connection");
            throw;
        }
    }

    /// <summary>
    /// Subscribes to progress updates for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to.</param>
    public async Task SubscribeToJobAsync(string jobId)
    {
        if (_hubConnection == null || _hubConnection.State != HubConnectionState.Connected)
        {
            await StartAsync();
        }

        if (!string.IsNullOrEmpty(_currentJobId) && _currentJobId != jobId)
        {
            try
            {
                await _hubConnection!.InvokeAsync("UnsubscribeFromJob", _currentJobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from job {JobId}", _currentJobId);
            }
        }

        _currentJobId = jobId;

        try
        {
            await _hubConnection!.InvokeAsync("SubscribeToJob", jobId);
            _logger.LogDebug("Subscribed to job {JobId}", jobId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to subscribe to job {JobId}", jobId);
            throw;
        }
    }

    /// <summary>
    /// Unsubscribes from the current job.
    /// </summary>
    public async Task UnsubscribeAsync()
    {
        if (_hubConnection != null && !string.IsNullOrEmpty(_currentJobId))
        {
            try
            {
                await _hubConnection.InvokeAsync("UnsubscribeFromJob", _currentJobId);
                _logger.LogDebug("Unsubscribed from job {JobId}", _currentJobId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unsubscribe from job {JobId}", _currentJobId);
            }
            finally
            {
                _currentJobId = null;
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_hubConnection != null)
        {
            try
            {
                await _hubConnection.DisposeAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing SignalR connection");
            }
        }
    }
}
