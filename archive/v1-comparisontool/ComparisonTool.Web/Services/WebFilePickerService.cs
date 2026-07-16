using ComparisonTool.Core.Abstractions;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Web fallback for native file picking. Browser-hosted UI uses InputFile instead.
/// </summary>
public sealed class WebFilePickerService : IFilePickerService
{
    /// <inheritdoc/>
    public bool IsAvailable => false;

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> PickFilesAsync(string title, string filter)
    {
        return Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
