using ComparisonTool.Core.Abstractions;

namespace ComparisonTool.Report.Services;

/// <summary>
/// No-op folder picker — folder browsing is not supported in a browser report.
/// </summary>
public sealed class WasmFolderPickerService : IFolderPickerService
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult<string?>(null);
}
