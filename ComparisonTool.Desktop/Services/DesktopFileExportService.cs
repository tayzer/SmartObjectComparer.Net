using System.IO;
using ComparisonTool.Core.Abstractions;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// Desktop implementation of file export using native Save File dialog.
/// </summary>
public class DesktopFileExportService : IFileExportService
{
    private readonly ILogger<DesktopFileExportService> _logger;

    public DesktopFileExportService(ILogger<DesktopFileExportService> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<bool> ExportAsync(string fileName, string content, string contentType)
    {
        try
        {
            var extension = Path.GetExtension(fileName);
            var filter = extension switch
            {
                ".json" => "JSON files (*.json)|*.json|All files (*.*)|*.*",
                ".md" => "Markdown files (*.md)|*.md|All files (*.*)|*.*",
                ".html" => "HTML files (*.html)|*.html|All files (*.*)|*.*",
                ".xml" => "XML files (*.xml)|*.xml|All files (*.*)|*.*",
                ".csv" => "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                _ => "All files (*.*)|*.*",
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = fileName,
                Filter = filter,
                Title = "Export File",
            };

            if (dialog.ShowDialog() == true)
            {
                File.WriteAllText(dialog.FileName, content);
                _logger.LogInformation("Exported file to {Path}", dialog.FileName);
                return Task.FromResult(true);
            }

            _logger.LogDebug("File export cancelled by user");
            return Task.FromResult(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export file {FileName}", fileName);
            return Task.FromResult(false);
        }
    }
}
