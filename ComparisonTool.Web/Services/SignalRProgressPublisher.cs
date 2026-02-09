using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Web.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace ComparisonTool.Web.Services;

/// <summary>
/// SignalR-based implementation of progress publisher.
/// </summary>
public class SignalRProgressPublisher : IComparisonProgressPublisher
{
    private readonly IHubContext<ComparisonProgressHub> _hubContext;
    private readonly ILogger<SignalRProgressPublisher> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRProgressPublisher"/> class.
    /// </summary>
    public SignalRProgressPublisher(
        IHubContext<ComparisonProgressHub> hubContext,
        ILogger<SignalRProgressPublisher> logger)
    {
        _hubContext = hubContext;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            await _hubContext.Clients.Group(update.JobId)
                .SendAsync("ProgressUpdate", update, cancellationToken);

            _logger.LogTrace(
                "Published progress for job {JobId}: {Phase} {Percent}% - {Message}",
                update.JobId,
                update.Phase,
                update.PercentComplete,
                update.Message);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish progress update for job {JobId}", update.JobId);
        }
    }
}
