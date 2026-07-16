using ComparisonTool.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// Desktop implementation of file picking using a native Windows dialog.
/// </summary>
public sealed class DesktopFilePickerService : IFilePickerService
{
    private readonly ILogger<DesktopFilePickerService> logger;

    public DesktopFilePickerService(ILogger<DesktopFilePickerService> logger)
    {
        this.logger = logger;
    }

    /// <inheritdoc/>
    public bool IsAvailable => true;

    /// <inheritdoc/>
    public async Task<IReadOnlyList<string>> PickFilesAsync(string title, string filter)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return this.PickFiles(title, filter);
        }

        return await dispatcher.InvokeAsync(() => this.PickFiles(title, filter));
    }

    private IReadOnlyList<string> PickFiles(string title, string filter)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = title,
                Filter = filter,
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
            };

            var owner = System.Windows.Application.Current?.MainWindow;
            var result = owner == null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (result == true)
            {
                this.logger.LogInformation("Selected {Count} request files through native desktop picker", dialog.FileNames.Length);
                return dialog.FileNames;
            }

            this.logger.LogDebug("Request file selection cancelled");
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            this.logger.LogError(ex, "Error showing request file picker dialog");
            return Array.Empty<string>();
        }
    }
}
