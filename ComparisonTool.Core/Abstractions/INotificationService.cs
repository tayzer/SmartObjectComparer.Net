namespace ComparisonTool.Core.Abstractions;

/// <summary>
/// Platform-agnostic notification/alert service.
/// Web implementation uses JS alert() or MudBlazor Snackbar.
/// Desktop implementation uses native message boxes or MudBlazor Snackbar.
/// </summary>
public interface INotificationService
{
    /// <summary>
    /// Shows an informational message to the user.
    /// </summary>
    Task ShowInfoAsync(string message);

    /// <summary>
    /// Shows an error message to the user.
    /// </summary>
    Task ShowErrorAsync(string message);

    /// <summary>
    /// Shows a success message to the user.
    /// </summary>
    Task ShowSuccessAsync(string message);
}
