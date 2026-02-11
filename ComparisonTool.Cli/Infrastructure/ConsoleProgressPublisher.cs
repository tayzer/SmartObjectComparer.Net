using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;

namespace ComparisonTool.Cli.Infrastructure;

/// <summary>
/// Publishes comparison progress updates to the console.
/// </summary>
public class ConsoleProgressPublisher : IComparisonProgressPublisher
{
    private int lastLineLength;

    /// <inheritdoc/>
    public Task PublishAsync(ComparisonProgressUpdate update, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return Task.CompletedTask;
        }

        var line = FormatProgressLine(update);

        // Overwrite the current line for a compact progress display
        var padding = lastLineLength > line.Length
            ? new string(' ', lastLineLength - line.Length)
            : string.Empty;

        Console.Write($"\r{line}{padding}");
        lastLineLength = line.Length;

        // Move to a new line when a phase completes or finishes
        if (update.Phase is ComparisonPhase.Completed or ComparisonPhase.Failed or ComparisonPhase.Cancelled)
        {
            Console.WriteLine();
        }

        return Task.CompletedTask;
    }

    private static string FormatProgressLine(ComparisonProgressUpdate update)
    {
        var phaseLabel = update.Phase switch
        {
            ComparisonPhase.Initializing => "Initializing",
            ComparisonPhase.Parsing => "Parsing",
            ComparisonPhase.Executing => "Executing",
            ComparisonPhase.Comparing => "Comparing",
            ComparisonPhase.Completed => "Done",
            ComparisonPhase.Failed => "FAILED",
            ComparisonPhase.Cancelled => "Cancelled",
            _ => update.Phase.ToString(),
        };

        var progressBar = BuildProgressBar(update.PercentComplete);
        var itemInfo = update.TotalItems > 0
            ? $" [{update.CompletedItems}/{update.TotalItems}]"
            : string.Empty;

        return $"  {phaseLabel}: {progressBar} {update.PercentComplete,3}%{itemInfo} {update.Message}";
    }

    private static string BuildProgressBar(int percent)
    {
        const int width = 20;
        var filled = (int)(percent / 100.0 * width);
        var empty = width - filled;
        return $"[{new string('#', filled)}{new string('-', empty)}]";
    }
}
