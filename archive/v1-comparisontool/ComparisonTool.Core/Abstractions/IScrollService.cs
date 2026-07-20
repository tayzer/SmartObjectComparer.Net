namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic scroll service.
/// Web implementation uses JS interop (scrollToElement).
/// Desktop implementation uses JS interop through BlazorWebView (same JS works).
/// </summary>
public interface IScrollService
{
    /// <summary>
    /// Scrolls to a DOM element by its ID.
    /// </summary>
    /// <param name="elementId">The HTML element ID to scroll to.</param>
    Task ScrollToElementAsync(string elementId);
}
