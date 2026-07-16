namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic folder picker service.
/// Web implementation uses JS interop to call .NET's FolderBrowserDialog via JSInvokable.
/// Desktop implementation uses native folder browser dialog directly.
/// </summary>
public interface IFolderPickerService
{
    /// <summary>
    /// Opens a folder picker dialog and returns the selected path.
    /// </summary>
    /// <param name="title">Dialog title/description.</param>
    /// <returns>The selected folder path, or null if cancelled.</returns>
    Task<string?> PickFolderAsync(string title);
}
