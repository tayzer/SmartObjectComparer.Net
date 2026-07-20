using ComparisonTool.Core.Abstractions;
using Microsoft.JSInterop;

namespace ComparisonTool.Report.Services;

/// <summary>
/// WASM implementation of scroll service — uses JS interop.
/// </summary>
public sealed class WasmScrollService : IScrollService
{
    private readonly IJSRuntime jsRuntime;

    public WasmScrollService(IJSRuntime jsRuntime)
    {
        this.jsRuntime = jsRuntime;
    }

    public async Task ScrollToElementAsync(string elementId)
    {
        try
        {
            await jsRuntime.InvokeVoidAsync("scrollToElement", elementId);
        }
        catch
        {
            // Scroll failure is non-critical in a report viewer.
        }
    }
}
