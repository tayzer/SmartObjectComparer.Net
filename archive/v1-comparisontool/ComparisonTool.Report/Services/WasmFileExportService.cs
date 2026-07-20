using ComparisonTool.Core.Abstractions;
using Microsoft.JSInterop;

namespace ComparisonTool.Report.Services;

/// <summary>
/// WASM implementation of file export — triggers a browser download via JS interop.
/// </summary>
public sealed class WasmFileExportService : IFileExportService
{
    private readonly IJSRuntime jsRuntime;

    public WasmFileExportService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public async Task<bool> ExportAsync(string fileName, string content, string contentType)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("saveAsFile", fileName, contentType, content);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
