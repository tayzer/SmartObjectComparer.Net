using ComparisonTool.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// Desktop notification service using MudBlazor Snackbar (injected at component level)
/// with a fallback to simple logging for non-UI scenarios.
/// </summary>
public class DesktopNotificationService : INotificationService
{
    private readonly ILogger<DesktopNotificationService> _logger;

    public DesktopNotificationService(ILogger<DesktopNotificationService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task ShowInfoAsync(string message)
    {
        _logger.LogInformation("[Notification] {Message}", message);
        System.Windows.MessageBox.Show(message, "Information",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ShowErrorAsync(string message)
    {
        _logger.LogError("[Notification] {Message}", message);
        System.Windows.MessageBox.Show(message, "Error",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task ShowSuccessAsync(string message)
    {
        _logger.LogInformation("[Notification] {Message}", message);
        System.Windows.MessageBox.Show(message, "Success",
            System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
        return Task.CompletedTask;
    }
}
