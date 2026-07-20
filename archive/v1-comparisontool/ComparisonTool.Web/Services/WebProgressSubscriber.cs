using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Adapter that exposes the SignalR progress service through the shared UI abstraction.
/// </summary>
public sealed class WebProgressSubscriber : IProgressSubscriber
{
    private readonly ComparisonProgressService progressService;

    public WebProgressSubscriber(ComparisonProgressService progressService)
    {
        this.progressService = progressService;
        this.progressService.OnProgressUpdate += HandleProgressUpdate;
    }

    public event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    public bool IsConnected => progressService.IsConnected;

    public Task StartAsync() => progressService.StartAsync();

    public Task SubscribeToJobAsync(string jobId) => progressService.SubscribeToJobAsync(jobId);

    public Task UnsubscribeAsync() => progressService.UnsubscribeAsync();

    public async ValueTask DisposeAsync()
    {
        progressService.OnProgressUpdate -= HandleProgressUpdate;
        await progressService.DisposeAsync();
    }

    private void HandleProgressUpdate(ComparisonProgressUpdate update) => OnProgressUpdate?.Invoke(update);
}