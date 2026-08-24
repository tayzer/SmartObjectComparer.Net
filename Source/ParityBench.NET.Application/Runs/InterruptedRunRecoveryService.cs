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
        InterruptedRunRecoveryResult result = await RecoverAsync(cancellationMessage, cancellationToken).ConfigureAwait(false);
        return result.CancelledRunCount;
    }

    public async Task<InterruptedRunRecoveryResult> RecoverAsync(
        string cancellationMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cancellationMessage);

        List<string> errors = new List<string>();
        IReadOnlyList<RunListItem> runs;
        try
        {
            runs = await runUseCases.ListRunsAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            errors.Add($"Interrupted-run recovery could not list saved runs: {ex.Message}");
            return new InterruptedRunRecoveryResult(0, await DrainWarningsAsync(cancellationToken).ConfigureAwait(false), errors);
        }

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
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                errors.Add($"Run '{run.Id.Value}' could not be cancelled during recovery: {ex.Message}");
            }
        }

        return new InterruptedRunRecoveryResult(
            cancelledCount,
            await DrainWarningsAsync(cancellationToken).ConfigureAwait(false),
            errors);
    }

    private Task<IReadOnlyList<RunSnapshotRecoveryWarning>> DrainWarningsAsync(CancellationToken cancellationToken) =>
        runUseCases.DrainRecoveryWarningsAsync(cancellationToken);

    private static bool IsTerminal(RunStatus status) =>
        status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;
}

public sealed record InterruptedRunRecoveryResult(
    int CancelledRunCount,
    IReadOnlyList<RunSnapshotRecoveryWarning> SnapshotWarnings,
    IReadOnlyList<string> Errors)
{
    public bool HasWarnings => SnapshotWarnings.Count > 0 || Errors.Count > 0;
}
