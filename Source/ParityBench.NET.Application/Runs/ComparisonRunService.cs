using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

public sealed class ComparisonRunService : IComparisonRunUseCases
{
    private readonly IRunStore runStore;
    private readonly IComparisonRunExecutor executor;
    private readonly IRunEventPublisher eventPublisher;
    private readonly IRunIdGenerator runIdGenerator;
    private readonly IRunCancellationRegistry runCancellationRegistry;
    private readonly IObservabilityRecorder observabilityRecorder;
    private readonly RetentionConfiguration retentionConfiguration;

    public ComparisonRunService(
        IRunStore runStore,
        IComparisonRunExecutor executor,
        IRunEventPublisher eventPublisher,
        IRunIdGenerator runIdGenerator,
        IRunCancellationRegistry runCancellationRegistry,
        IObservabilityRecorder? observabilityRecorder = null,
        IOptions<RetentionConfiguration>? retentionConfigurationOptions = null)
    {
        this.runStore = runStore;
        this.executor = executor;
        this.eventPublisher = eventPublisher;
        this.runIdGenerator = runIdGenerator;
        this.runCancellationRegistry = runCancellationRegistry;
        this.observabilityRecorder = observabilityRecorder ?? NoOpObservabilityRecorder.Instance;
        retentionConfiguration = retentionConfigurationOptions?.Value ?? RetentionConfiguration.Default;
    }

    public async Task<ComparisonRun> CreateRunAsync(
        RunOptions options,
        CancellationToken cancellationToken = default)
    {
        RunId runId = runIdGenerator.CreateId();
        ComparisonRun run = ComparisonRun.Create(
            runId,
            options,
            runRetentionMode: options.RunRetentionModeOverride ?? retentionConfiguration.Mode,
            runRetentionPolicyVersion: RetentionConfiguration.PolicyVersionV1,
            comparisonRulesSnapshotHash: options.ComparisonRulesSnapshotHash);

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

        CancellationToken executionToken = runCancellationRegistry.CreateLinkedToken(runId, cancellationToken);
        RunProgressReporter reporter = new RunProgressReporter(currentRun.Options.LargeRun, async (status, progress, token) =>
        {
            currentRun = currentRun.Advance(status, progress);
            await SaveAndPublishAsync(currentRun, token).ConfigureAwait(false);
        });

        try
        {
            RunResultSummary summary = await executor
                .ExecuteAsync(currentRun, reporter, executionToken)
                .ConfigureAwait(false);
            executionToken.ThrowIfCancellationRequested();

            currentRun = currentRun.Complete(summary, diagnostics: observabilityRecorder.CreateSnapshot(runId));
            await SaveAndPublishAsync(currentRun, cancellationToken).ConfigureAwait(false);
            return currentRun;
        }
        catch (OperationCanceledException)
        {
            currentRun = currentRun.Cancel();
            await SaveAndPublishAsync(currentRun, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        catch (Exception ex) when (runCancellationRegistry.IsCancellationRequested(runId))
        {
            observabilityRecorder.RecordException(runId, "RunCancellation", ex);
            currentRun = currentRun.Cancel($"Run was cancelled after executor error: {ex.Message}");
            await SaveAndPublishAsync(currentRun, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        catch (Exception ex)
        {
            observabilityRecorder.RecordException(runId, "RunExecution", ex);
            currentRun = currentRun.Fail(ex.Message, diagnostics: observabilityRecorder.CreateSnapshot(runId));
            await SaveAndPublishAsync(currentRun, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        finally
        {
            runCancellationRegistry.Complete(runId);
        }
    }

    public async Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await LoadRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
        runCancellationRegistry.RequestCancellation(runId);
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
        private readonly LargeRunOptions options;
        private readonly Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync;
        private readonly object syncRoot = new object();
        private DateTimeOffset lastPublishedAt = DateTimeOffset.MinValue;
        private int lastPublishedCompletedItems;
        private RunStatus? lastPublishedStatus;

        public RunProgressReporter(
            LargeRunOptions options,
            Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync)
        {
            this.options = options;
            this.reportAsync = reportAsync;
        }

        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default) =>
            ReportAsync(status, progress, cancellationToken, false);

        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default,
            bool force = false)
        {
            if (!force && !ShouldPublish(status, progress))
            {
                return Task.CompletedTask;
            }

            return reportAsync(status, progress, cancellationToken);
        }

        private bool ShouldPublish(RunStatus status, RunProgress progress)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            int completed = progress.CompletedItems ?? 0;
            lock (syncRoot)
            {
                bool statusChanged = lastPublishedStatus != status;
                bool itemIntervalReached = completed - lastPublishedCompletedItems >= options.ProgressUpdateItemInterval;
                bool timeIntervalReached = now - lastPublishedAt >= TimeSpan.FromMilliseconds(options.ProgressUpdateMillisecondsInterval);
                if (!statusChanged && !itemIntervalReached && !timeIntervalReached)
                {
                    return false;
                }

                lastPublishedAt = now;
                lastPublishedCompletedItems = completed;
                lastPublishedStatus = status;
                return true;
            }
        }
    }
}