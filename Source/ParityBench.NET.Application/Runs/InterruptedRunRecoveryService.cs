using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

/// <summary>
/// Marks persisted non-terminal runs as cancelled when an interactive host cannot
/// safely continue their in-process execution.
/// </summary>
public sealed class InterruptedRunRecoveryService
{
    public const string StartupCancellationMessage = "Run was cancelled because the desktop app restarted before it finished.";
    public const string SuspendCancellationMessage = "Run was cancelled because Windows suspended the desktop app.";
    public const string ShutdownCancellationMessage = "Run was cancelled because the desktop app closed before it finished.";

    private readonly IComparisonRunUseCases runUseCases;

    public InterruptedRunRecoveryService(IComparisonRunUseCases runUseCases)
    {
        this.runUseCases = runUseCases ?? throw new ArgumentNullException(nameof(runUseCases));
    }

    public async Task<int> CancelNonTerminalRunsAsync(
        string cancellationMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cancellationMessage);

        IReadOnlyList<RunListItem> runs = await runUseCases.ListRunsAsync(cancellationToken).ConfigureAwait(false);
        int cancelledCount = 0;
        foreach (RunListItem run in runs.Where(run => !IsTerminal(run.Status)))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await runUseCases
                    .CancelRunAsync(run.Id, cancellationMessage, cancellationToken)
                    .ConfigureAwait(false);
                cancelledCount++;
            }
            catch (InvalidRunStateException)
            {
                // A live run may finish between listing and cancellation.
            }
        }

        return cancelledCount;
    }

    private static bool IsTerminal(RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;
}
