namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic file picker service.
/// Desktop implementations can use native dialogs to avoid browser/WebView file picker limitations.
/// </summary>
public interface IFilePickerService
{
    /// <summary>
    /// Gets a value indicating whether this platform can pick files natively.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Opens a file picker dialog and returns selected absolute file paths.
    /// </summary>
    /// <param name="title">Dialog title.</param>
    /// <param name="filter">Dialog file filter.</param>
    /// <returns>The selected file paths, or an empty list if cancelled or unavailable.</returns>
    Task<IReadOnlyList<string>> PickFilesAsync(string title, string filter);
}
