using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Report.Services;

/// <summary>
/// No-op progress subscriber — the report viewer doesn't run live comparisons.
/// </summary>
public sealed class WasmProgressSubscriber : IProgressSubscriber
{
    public event Action<ComparisonProgressUpdate>? OnProgressUpdate;

    public bool IsConnected => false;

    public Task StartAsync() => Task.CompletedTask;

    public Task SubscribeToJobAsync(string jobId) => Task.CompletedTask;

    public Task UnsubscribeAsync() => Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
