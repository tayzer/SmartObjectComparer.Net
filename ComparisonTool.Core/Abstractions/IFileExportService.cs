namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic file export service.
/// Web implementation uses JS interop (saveAsFile/downloadFile).
/// Desktop implementation uses native save file dialogs.
/// </summary>
public interface IFileExportService
{
    /// <summary>
    /// Exports content to a file. In web, triggers a browser download.
    /// In desktop, shows a save file dialog.
    /// </summary>
    /// <param name="fileName">Suggested file name.</param>
    /// <param name="content">File content as a string.</param>
    /// <param name="contentType">MIME type (e.g., "application/json", "text/markdown").</param>
    /// <returns>True if the file was saved successfully.</returns>
    Task<bool> ExportAsync(string fileName, string content, string contentType);
}
