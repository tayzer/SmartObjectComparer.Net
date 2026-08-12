using System.Collections.Concurrent;

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
    private readonly ConcurrentDictionary<RunId, SemaphoreSlim> runTransitionGates = new ConcurrentDictionary<RunId, SemaphoreSlim>();

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
        ComparisonRun currentRun = await TransitionRunAsync(runId, run => run.Start(), allowTerminal: false, cancellationToken).ConfigureAwait(false);

        CancellationToken executionToken = runCancellationRegistry.CreateLinkedToken(runId, cancellationToken);
        currentRun = await LoadRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
        if (currentRun.IsTerminal)
        {
            // Cancellation may land after Start() persisted but before the
            // in-memory token was registered. Do not launch the executor then.
            runCancellationRegistry.Complete(runId);
            return currentRun;
        }

        RunProgressReporter reporter = new RunProgressReporter(currentRun.Options.LargeRun, async (status, progress, token) =>
        {
            currentRun = await TransitionRunAsync(
                runId,
                run => run.Advance(status, progress),
                allowTerminal: true,
                token).ConfigureAwait(false);
        });

        try
        {
            RunResultSummary summary = await executor
                .ExecuteAsync(currentRun, reporter, executionToken)
                .ConfigureAwait(false);
            executionToken.ThrowIfCancellationRequested();

            currentRun = await TransitionRunAsync(
                runId,
                run => run.Complete(summary, diagnostics: observabilityRecorder.CreateSnapshot(runId)),
                allowTerminal: true,
                cancellationToken).ConfigureAwait(false);
            return currentRun;
        }
        catch (OperationCanceledException)
        {
            currentRun = await TransitionRunAsync(runId, run => run.Cancel(), allowTerminal: true, CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        catch (Exception ex) when (runCancellationRegistry.IsCancellationRequested(runId))
        {
            observabilityRecorder.RecordException(runId, "RunCancellation", ex);
            currentRun = await TransitionRunAsync(
                runId,
                run => run.Cancel($"Run was cancelled after executor error: {ex.Message}"),
                allowTerminal: true,
                CancellationToken.None).ConfigureAwait(false);
            return currentRun;
        }
        catch (Exception ex)
        {
            observabilityRecorder.RecordException(runId, "RunExecution", ex);
            currentRun = await TransitionRunAsync(
                runId,
                run => run.Fail(ex.Message, diagnostics: observabilityRecorder.CreateSnapshot(runId)),
                allowTerminal: true,
                CancellationToken.None).ConfigureAwait(false);
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
        => await CancelRunAsync(runId, cancellationMessage: null, cancellationToken).ConfigureAwait(false);

    public async Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default)
    {
        runCancellationRegistry.RequestCancellation(runId);
        return await TransitionRunAsync(runId, run => run.Cancel(cancellationMessage), allowTerminal: false, cancellationToken).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        runStore.ListAsync(cancellationToken);

    public Task<IReadOnlyList<RunSnapshotRecoveryWarning>> DrainRecoveryWarningsAsync(
        CancellationToken cancellationToken = default) =>
        runStore.DrainRecoveryWarningsAsync(cancellationToken);

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

    private async Task<ComparisonRun> TransitionRunAsync(
        RunId runId,
        Func<ComparisonRun, ComparisonRun> transition,
        bool allowTerminal,
        CancellationToken cancellationToken)
    {
        SemaphoreSlim gate = runTransitionGates.GetOrAdd(runId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ComparisonRun currentRun = await LoadRequiredRunAsync(runId, cancellationToken).ConfigureAwait(false);
            if (currentRun.IsTerminal && allowTerminal)
            {
                return currentRun;
            }

            ComparisonRun updatedRun = transition(currentRun);
            await SaveAndPublishAsync(updatedRun, cancellationToken).ConfigureAwait(false);
            return updatedRun;
        }
        finally
        {
            gate.Release();
        }
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
}
