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
    private readonly IHubContext<ComparisonProgressHub> hubContext;
    private readonly ILogger<SignalRProgressPublisher> logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SignalRProgressPublisher"/> class.
    /// </summary>
    public SignalRProgressPublisher(
        IHubContext<ComparisonProgressHub> hubContext,
        ILogger<SignalRProgressPublisher> logger)
    {
        this.hubContext = hubContext;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)
    {
        try
        {
            await hubContext.Clients.Group(update.JobId)
                .SendAsync("ProgressUpdate", update, cancellationToken);

            logger.LogTrace(
                "Published progress for job {JobId}: {Phase} {Percent}% - {Message}",
                update.JobId,
                update.Phase,
                update.PercentComplete,
                update.Message);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to publish progress update for job {JobId}", update.JobId);
        }
    }
}
