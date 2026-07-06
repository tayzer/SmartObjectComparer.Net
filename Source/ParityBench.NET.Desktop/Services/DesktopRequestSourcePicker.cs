using System.Threading;
using System.Windows;

using Microsoft.Extensions.Logging;

using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.Desktop.Services;

public sealed class DesktopRequestSourcePicker : IRequestSourcePicker
{
    private const string RequestFileFilter = "Request files (*.xml;*.json;*.txt)|*.xml;*.json;*.txt|All files (*.*)|*.*";
    private readonly ILogger<DesktopRequestSourcePicker> logger;

    public DesktopRequestSourcePicker(ILogger<DesktopRequestSourcePicker> logger)
    {
        this.logger = logger;
    }

    public bool IsAvailable => true;

    public async Task<IReadOnlyList<string>> PickRequestFilesAsync(CancellationToken cancellationToken = default)
    {
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            return PickFiles();
        }

        return await dispatcher.InvokeAsync(PickFiles).Task.ConfigureAwait(false);
    }

    public Task<string?> PickRequestDirectoryAsync(CancellationToken cancellationToken = default)
    {
        TaskCompletionSource<string?> completion = new TaskCompletionSource<string?>();
        Thread thread = new Thread(() =>
        {
            try
            {
                using System.Windows.Forms.FolderBrowserDialog dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = "Select request folder",
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                };

                System.Windows.Forms.DialogResult result = dialog.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    logger.LogInformation("Selected request folder: {Path}", dialog.SelectedPath);
                    completion.SetResult(dialog.SelectedPath);
                    return;
                }

                logger.LogDebug("Request folder selection cancelled");
                completion.SetResult(null);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error showing request folder picker dialog");
                completion.SetResult(null);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private IReadOnlyList<string> PickFiles()
    {
        try
        {
            Microsoft.Win32.OpenFileDialog dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select request files",
                Filter = RequestFileFilter,
                Multiselect = true,
                CheckFileExists = true,
                CheckPathExists = true,
            };

            Window? owner = System.Windows.Application.Current?.MainWindow;
            bool? result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
            if (result == true)
            {
                logger.LogInformation("Selected {Count} request files", dialog.FileNames.Length);
                return dialog.FileNames;
            }

            logger.LogDebug("Request file selection cancelled");
            return Array.Empty<string>();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error showing request file picker dialog");
            return Array.Empty<string>();
        }
    }
}
