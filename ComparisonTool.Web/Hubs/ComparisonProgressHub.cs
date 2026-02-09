using Microsoft.AspNetCore.SignalR;

namespace ComparisonTool.Web.Hubs;

/// <summary>
/// SignalR hub for comparison progress updates.
/// </summary>
public class ComparisonProgressHub : Hub
{
    private readonly ILogger<ComparisonProgressHub> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ComparisonProgressHub"/> class.
    /// </summary>
    public ComparisonProgressHub(ILogger<ComparisonProgressHub> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Subscribe to progress updates for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID to subscribe to.</param>
    public async Task SubscribeToJob(string jobId)
    {
        await Groups.AddToGroupAsync(Context.ConnectionId, jobId);
        _logger.LogDebug("Client {ConnectionId} subscribed to job {JobId}", Context.ConnectionId, jobId);
    }

    /// <summary>
    /// Unsubscribe from progress updates for a specific job.
    /// </summary>
    /// <param name="jobId">The job ID to unsubscribe from.</param>
    public async Task UnsubscribeFromJob(string jobId)
    {
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, jobId);
        _logger.LogDebug("Client {ConnectionId} unsubscribed from job {JobId}", Context.ConnectionId, jobId);
    }

    /// <inheritdoc/>
    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogDebug("Client {ConnectionId} disconnected", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
