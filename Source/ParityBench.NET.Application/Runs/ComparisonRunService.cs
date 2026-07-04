using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

public sealed class ComparisonRunService : IComparisonRunUseCases
{
    private readonly IRunStore runStore;
    private readonly IComparisonRunExecutor executor;
    private readonly IRunEventPublisher eventPublisher;
    private readonly IRunIdGenerator runIdGenerator;

    public ComparisonRunService(
        IRunStore runStore,
        IComparisonRunExecutor executor,
        IRunEventPublisher eventPublisher,
        IRunIdGenerator runIdGenerator)
    {
        this.runStore = runStore;
        this.executor = executor;
        this.eventPublisher = eventPublisher;
        this.runIdGenerator = runIdGenerator;
    }

    public async Task<ComparisonRun> CreateRunAsync(
        RunOptions options,
        CancellationToken cancellationToken = default)
    {
        RunId runId = runIdGenerator.CreateId();
        ComparisonRun run = ComparisonRun.Create(runId, options);

        await runStore.SaveAsync(run, cancellationToken).ConfigureAwait(false);
        return run;
    }

    public async Task<ComparisonRun> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun currentRun = await LoadRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
        currentRun = currentRun.Start();
        await SaveAndPublishAsync(currentRun, cancellationToken).ConfigureAwait(false);

        RunProgressReporter reporter = new RunProgressReporter(async (status, progress, token) =>
        {
            currentRun = currentRun.Advance(status, progress);
            await SaveAndPublishAsync(currentRun, token).ConfigureAwait(false);
        });

        try
        {
            RunResultSummary summary = await executor
                .ExecuteAsync(currentRun, reporter, cancellationToken)
                .ConfigureAwait(false);

            currentRun = currentRun.Complete(summary);
            await SaveAndPublishAsync(currentRun, cancellationToken).ConfigureAwait(false);
            return currentRun;
        }
        catch (OperationCanceledException)
        {
            currentRun = currentRun.Cancel();
            await SaveAndPublishAsync(currentRun, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        catch (Exception ex)
        {
            currentRun = currentRun.Fail(ex.Message);
            await SaveAndPublishAsync(currentRun, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
    }

    public async Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await LoadRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
        ComparisonRun cancelledRun = run.Cancel();

        await SaveAndPublishAsync(cancelledRun, cancellationToken).ConfigureAwait(false);
        return cancelledRun;
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        runStore.ListAsync(cancellationToken);

    public Task<RunResultSummary?> LoadRunSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        runStore.LoadSummaryAsync(runId, cancellationToken);

    private async Task<ComparisonRun> LoadRequiredRunAsync(
        RunId runId,
        CancellationToken cancellationToken)
    {
        ComparisonRun? run = await runStore.LoadAsync(runId, cancellationToken).ConfigureAwait(false);
        return run ?? throw new RunNotFoundException(runId);
    }

    private async Task SaveAndPublishAsync(
        ComparisonRun run,
        CancellationToken cancellationToken)
    {
        await runStore.SaveAsync(run, cancellationToken).ConfigureAwait(false);
        await eventPublisher
            .PublishAsync(new RunEvent(run.Id, run.Status, run.Progress, run.UpdatedAt, run.ErrorMessage), cancellationToken)
            .ConfigureAwait(false);
    }

    private sealed class RunProgressReporter : IRunProgressReporter
    {
        private readonly Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync;

        public RunProgressReporter(Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync)
        {
            this.reportAsync = reportAsync;
        }

        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default) =>
            reportAsync(status, progress, cancellationToken);
    }
}
