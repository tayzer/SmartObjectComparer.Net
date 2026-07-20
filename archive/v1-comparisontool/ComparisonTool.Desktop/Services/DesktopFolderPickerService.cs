using System;
using System.Threading;
using System.Threading.Tasks;
using ComparisonTool.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// Desktop implementation of folder picker using Windows FolderBrowserDialog.
/// </summary>
public class DesktopFolderPickerService : IFolderPickerService
{
    private readonly ILogger<DesktopFolderPickerService> _logger;

    public DesktopFolderPickerService(ILogger<DesktopFolderPickerService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<string?> PickFolderAsync(string title)
    {
        var tcs = new TaskCompletionSource<string?>();

        var thread = new Thread(() =>
        {
            try
            {
                var dialog = new System.Windows.Forms.FolderBrowserDialog
                {
                    Description = title,
                    UseDescriptionForTitle = true,
                    ShowNewFolderButton = false,
                };

                var result = dialog.ShowDialog();

                if (result == System.Windows.Forms.DialogResult.OK)
                {
                    _logger.LogDebug("Folder selected: {Path}", dialog.SelectedPath);
                    tcs.SetResult(dialog.SelectedPath);
                }
                else
                {
                    _logger.LogDebug("Folder selection cancelled");
                    tcs.SetResult(null);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing folder picker dialog");
                tcs.SetResult(null);
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();

        return tcs.Task;
    }
}
