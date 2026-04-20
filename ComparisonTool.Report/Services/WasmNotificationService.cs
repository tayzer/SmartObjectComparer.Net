using ComparisonTool.Core.Abstractions;

namespace ComparisonTool.Report.Services;

/// <summary>
/// No-op notification service for the read-only report viewer.
/// </summary>
public sealed class WasmNotificationService : INotificationService
{
    public Task ShowInfoAsync(string message) => Task.CompletedTask;

    public Task ShowErrorAsync(string message) => Task.CompletedTask;

    public Task ShowSuccessAsync(string message) => Task.CompletedTask;
}
