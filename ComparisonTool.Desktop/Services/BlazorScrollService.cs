using ComparisonTool.Core.Abstractions;
using Microsoft.JSInterop;

namespace ComparisonTool.Desktop.Services;

/// <summary>
/// Scroll service that uses JS interop through the BlazorWebView.
/// The same scrollToElement JS function works in both browser and WebView2.
/// </summary>
public class BlazorScrollService : IScrollService
{
    private readonly IJSRuntime _jsRuntime;

    public BlazorScrollService(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <inheritdoc/>
    public async Task ScrollToElementAsync(string elementId)
    {
        try
        {
            await _jsRuntime.InvokeVoidAsync("scrollToElement", elementId);
        }
        catch
        {
            // Scroll failures are non-critical; suppress.
        }
    }
}
