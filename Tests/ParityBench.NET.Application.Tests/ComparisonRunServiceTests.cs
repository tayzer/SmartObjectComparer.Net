using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Extensions.Options;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class ComparisonRunServiceTests
{
    [TestMethod]
    public async Task CreateRun_WhenOptionsAreValid_PersistsCreatedRun()
    {
        FakeRunStore store = new FakeRunStore();
        ComparisonRunService service = CreateService(store);

        ComparisonRun run = await service.CreateRunAsync(CreateOptions());

        ComparisonRun? storedRun = await store.LoadAsync(run.Id);
        Assert.IsNotNull(storedRun);
        Assert.AreEqual(RunStatus.Created, storedRun.Status);
        Assert.AreEqual(run.Id, storedRun.Id);
        Assert.AreEqual(RetentionMode.TrimmedEqualsAndIgnoredPaths, storedRun.RunRetentionMode);
        Assert.AreEqual(RetentionConfiguration.PolicyVersionV1, storedRun.RunRetentionPolicyVersion);
    }

    [TestMethod]
    public async Task CreateRun_WhenRetentionModeDefaultIsConfigured_UsesConfiguredRunRetentionMode()
    {
        FakeRunStore store = new FakeRunStore();
        RetentionConfiguration retentionConfiguration = new RetentionConfiguration
        {
            Mode = RetentionMode.None,
        };
        ComparisonRunService service = CreateService(store, retentionConfiguration: retentionConfiguration);

        ComparisonRun run = await service.CreateRunAsync(CreateOptions());

        Assert.AreEqual(RetentionMode.None, run.RunRetentionMode);
        Assert.AreEqual(RetentionConfiguration.PolicyVersionV1, run.RunRetentionPolicyVersion);
    }

    [TestMethod]
    public async Task CreateRun_WhenRunOptionOverrideExists_UsesOverrideInsteadOfConfiguredDefault()
    {
        FakeRunStore store = new FakeRunStore();
        RetentionConfiguration retentionConfiguration = new RetentionConfiguration
        {
            Mode = RetentionMode.TrimmedEquals,
        };
        ComparisonRunService service = CreateService(store, retentionConfiguration: retentionConfiguration);
        RunOptions options = CreateOptions(runRetentionModeOverride: RetentionMode.None);

        ComparisonRun run = await service.CreateRunAsync(options);

        Assert.AreEqual(RetentionMode.None, run.RunRetentionMode);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorCompletes_MarksRunCompletedAndPublishesEvents()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunEventPublisher eventPublisher = new FakeRunEventPublisher();
        RunResultSummary expectedSummary = CreateSummary();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = (_, _, _) => Task.FromResult(expectedSummary),
        };
        ComparisonRunService service = CreateService(store, executor, eventPublisher);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun completedRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Completed, completedRun.Status);
        Assert.AreEqual(expectedSummary, completedRun.Summary);
        CollectionAssert.Contains(store.SavedStatuses, RunStatus.Executing);
        CollectionAssert.Contains(store.SavedStatuses, RunStatus.Completed);
        CollectionAssert.Contains(eventPublisher.PublishedStatuses, RunStatus.Executing);
        CollectionAssert.Contains(eventPublisher.PublishedStatuses, RunStatus.Completed);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorReportsProgress_PersistsLatestProgress()
    {
        FakeRunStore store = new FakeRunStore();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = async (_, progressReporter, cancellationToken) =>
            {
                await progressReporter
                    .ReportAsync(RunStatus.Parsing, new RunProgress(10, "Parsing requests.", 1, 10), cancellationToken)
                    .ConfigureAwait(false);
                await progressReporter
                    .ReportAsync(RunStatus.Comparing, new RunProgress(75, "Comparing responses.", 7, 10), cancellationToken)
                    .ConfigureAwait(false);

                return CreateSummary();
            },
        };
        ComparisonRunService service = CreateService(store, executor);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        await service.StartRunAsync(run.Id);

        ComparisonRun? comparingSnapshot = store.SavedRuns.SingleOrDefault(savedRun =>
            savedRun.Status == RunStatus.Comparing);
        Assert.IsNotNull(comparingSnapshot);
        Assert.AreEqual(75, comparingSnapshot.Progress.PercentComplete);
        Assert.AreEqual("Comparing responses.", comparingSnapshot.Progress.Message);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorThrows_MarksRunFailed()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunEventPublisher eventPublisher = new FakeRunEventPublisher();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = (_, _, _) => throw new InvalidOperationException("Execution failed."),
        };
        ComparisonRunService service = CreateService(store, executor, eventPublisher);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun failedRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Failed, failedRun.Status);
        Assert.AreEqual("Execution failed.", failedRun.ErrorMessage);
        CollectionAssert.Contains(eventPublisher.PublishedStatuses, RunStatus.Failed);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorIsCancelled_MarksRunCancelled()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunEventPublisher eventPublisher = new FakeRunEventPublisher();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = (_, _, _) => throw new OperationCanceledException(),
        };
        ComparisonRunService service = CreateService(store, executor, eventPublisher);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.Contains(eventPublisher.PublishedStatuses, RunStatus.Cancelled);
    }

    [TestMethod]
    public async Task StartRun_WhenCancelIsRequestedByRunId_MarksRunCancelled()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunCancellationRegistry cancellationRegistry = new FakeRunCancellationRegistry();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = async (run, _, cancellationToken) =>
            {
                cancellationRegistry.RequestCancellation(run.Id);
                await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                return CreateSummary();
            },
        };
        ComparisonRunService service = CreateService(store, executor, runCancellationRegistry: cancellationRegistry);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.Contains(cancellationRegistry.RequestedRuns, run.Id);
        CollectionAssert.Contains(cancellationRegistry.CompletedRuns, run.Id);
        CollectionAssert.DoesNotContain(store.SavedStatuses, RunStatus.Completed);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorCancelsAfterProgress_DoesNotCompleteRun()
    {
        FakeRunStore store = new FakeRunStore();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = async (_, progressReporter, cancellationToken) =>
            {
                await progressReporter
                    .ReportAsync(RunStatus.Executing, new RunProgress(50, "Halfway.", 1, 2), cancellationToken)
                    .ConfigureAwait(false);
                throw new OperationCanceledException();
            },
        };
        ComparisonRunService service = CreateService(store, executor);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.DoesNotContain(store.SavedStatuses, RunStatus.Completed);
    }

    [TestMethod]
    public async Task StartRun_WhenExecutorFailsAfterCancellationRequest_MarksCancelledNotFailed()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunCancellationRegistry cancellationRegistry = new FakeRunCancellationRegistry();
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = (run, _, _) =>
            {
                cancellationRegistry.RequestCancellation(run.Id);
                throw new InvalidOperationException("Late executor failure.");
            },
        };
        ComparisonRunService service = CreateService(store, executor, runCancellationRegistry: cancellationRegistry);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.StartRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.DoesNotContain(store.SavedStatuses, RunStatus.Failed);
    }

    [TestMethod]
    public async Task StartRun_WhenRunCompletes_UnregistersCancellation()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunCancellationRegistry cancellationRegistry = new FakeRunCancellationRegistry();
        ComparisonRunService service = CreateService(store, runCancellationRegistry: cancellationRegistry);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        await service.StartRunAsync(run.Id);

        CollectionAssert.Contains(cancellationRegistry.CompletedRuns, run.Id);
        Assert.IsFalse(cancellationRegistry.IsCancellationRequested(run.Id));
    }

    [TestMethod]
    public async Task StartRun_WhenRunDoesNotExist_ThrowsRunNotFoundException()
    {
        ComparisonRunService service = CreateService(new FakeRunStore());

        await AssertThrowsAsync<RunNotFoundException>(() =>
            service.StartRunAsync(new RunId("missing-run")));
    }

    [TestMethod]
    public async Task CancelRun_WhenRunIsActive_MarksRunCancelled()
    {
        FakeRunStore store = new FakeRunStore();
        ComparisonRunService service = CreateService(store);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start();
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.CancelRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.Contains(store.SavedStatuses, RunStatus.Cancelled);
    }

    [TestMethod]
    public async Task CancelRun_WhenRunIsExecuting_RequestsCancellationAndPublishesCancelled()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunCancellationRegistry cancellationRegistry = new FakeRunCancellationRegistry();
        FakeRunEventPublisher eventPublisher = new FakeRunEventPublisher();
        ComparisonRunService service = CreateService(store, eventPublisher: eventPublisher, runCancellationRegistry: cancellationRegistry);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start();
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.CancelRunAsync(run.Id);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        CollectionAssert.Contains(cancellationRegistry.RequestedRuns, run.Id);
        CollectionAssert.Contains(eventPublisher.PublishedStatuses, RunStatus.Cancelled);
    }

    [TestMethod]
    public async Task CancelRun_WhenCancellationMessageIsSupplied_PersistsAndPublishesMessage()
    {
        FakeRunStore store = new FakeRunStore();
        FakeRunEventPublisher eventPublisher = new FakeRunEventPublisher();
        ComparisonRunService service = CreateService(store, eventPublisher: eventPublisher);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start();
        await store.SaveAsync(run);

        ComparisonRun cancelledRun = await service.CancelRunAsync(run.Id, "Desktop app was interrupted.");

        Assert.AreEqual("Desktop app was interrupted.", cancelledRun.Progress.Message);
        Assert.AreEqual("Desktop app was interrupted.", store.SavedRuns.Last().Progress.Message);
        Assert.AreEqual("Desktop app was interrupted.", eventPublisher.PublishedEvents.Last().Progress.Message);
    }

    [TestMethod]
    public async Task StartRun_WhenCancellationWinsBeforeLateProgress_DoesNotRestoreActiveStatus()
    {
        FakeRunStore store = new FakeRunStore();
        TaskCompletionSource executorStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource releaseExecutor = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        FakeComparisonRunExecutor executor = new FakeComparisonRunExecutor
        {
            ExecuteAsyncCore = async (_, progressReporter, _) =>
            {
                executorStarted.SetResult();
                await releaseExecutor.Task.ConfigureAwait(false);
                await progressReporter.ReportAsync(
                    RunStatus.Comparing,
                    new RunProgress(50, "Late progress.", 1, 2),
                    CancellationToken.None).ConfigureAwait(false);
                return CreateSummary();
            },
        };
        ComparisonRunService service = CreateService(store, executor);
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions());
        await store.SaveAsync(run);

        Task<ComparisonRun> startTask = service.StartRunAsync(run.Id);
        await executorStarted.Task.ConfigureAwait(false);
        ComparisonRun cancelledRun = await service.CancelRunAsync(run.Id, "Desktop suspended.");
        releaseExecutor.SetResult();
        ComparisonRun finalRun = await startTask.ConfigureAwait(false);

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        Assert.AreEqual(RunStatus.Cancelled, finalRun.Status);
        Assert.AreEqual(RunStatus.Cancelled, (await store.LoadAsync(run.Id))?.Status);
        Assert.AreEqual("Desktop suspended.", (await store.LoadAsync(run.Id))?.Progress.Message);
    }

    [TestMethod]
    public async Task CancelRun_WhenRunIsCompleted_ThrowsInvalidRunStateException()
    {
        FakeRunStore store = new FakeRunStore();
        ComparisonRunService service = CreateService(store);
        ComparisonRun run = ComparisonRun
            .Create(new RunId("run-1"), CreateOptions())
            .Start()
            .Complete(CreateSummary());
        await store.SaveAsync(run);

        await AssertThrowsAsync<InvalidRunStateException>(() => service.CancelRunAsync(run.Id));
    }

    [TestMethod]
    public async Task ListRuns_WhenStoreHasRuns_ReturnsRunSummaries()
    {
        FakeRunStore store = new FakeRunStore();
        ComparisonRunService service = CreateService(store);
        await store.SaveAsync(ComparisonRun.Create(new RunId("run-1"), CreateOptions()));
        await store.SaveAsync(ComparisonRun.Create(new RunId("run-2"), CreateOptions()).Start());

        IReadOnlyList<RunListItem> runs = await service.ListRunsAsync();

        Assert.AreEqual(2, runs.Count);
        CollectionAssert.AreEquivalent(
            new[] { RunStatus.Created, RunStatus.Executing },
            runs.Select(run => run.Status).ToArray());
    }

    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private static ComparisonRunService CreateService(
        FakeRunStore store,
        FakeComparisonRunExecutor? executor = null,
        FakeRunEventPublisher? eventPublisher = null,
        FakeRunIdGenerator? runIdGenerator = null,
        FakeRunCancellationRegistry? runCancellationRegistry = null,
        RetentionConfiguration? retentionConfiguration = null) =>
        new ComparisonRunService(
            store,
            executor ?? new FakeComparisonRunExecutor(),
            eventPublisher ?? new FakeRunEventPublisher(),
            runIdGenerator ?? new FakeRunIdGenerator(new RunId("generated-run")),
            runCancellationRegistry ?? new FakeRunCancellationRegistry(),
            retentionConfigurationOptions: Options.Create(retentionConfiguration ?? new RetentionConfiguration()));

    private static RunOptions CreateOptions(RetentionMode? runRetentionModeOverride = null) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            8,
            runRetentionModeOverride: runRetentionModeOverride);

    private static RunResultSummary CreateSummary() =>
        new RunResultSummary(totalPairs: 2, equalPairs: 1, differentPairs: 1, errorPairs: 0);

    private sealed class FakeRunStore : IRunStore
    {
        private readonly Dictionary<RunId, ComparisonRun> runs = new Dictionary<RunId, ComparisonRun>();

        public List<ComparisonRun> SavedRuns { get; } = new List<ComparisonRun>();

        public List<RunStatus> SavedStatuses => SavedRuns.Select(run => run.Status).ToList();

        public Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
        {
            runs[run.Id] = run;
            SavedRuns.Add(run);
            return Task.CompletedTask;
        }

        public Task<ComparisonRun?> LoadAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run);
        }

        public Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RunListItem> listItems = runs
                .Values
                .Select(RunListItem.FromRun)
                .OrderBy(run => run.Id.Value, StringComparer.Ordinal)
                .ToList();

            return Task.FromResult(listItems);
        }

        public Task<RunResultSummary?> LoadSummaryAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run?.Summary);
        }
    }

    private sealed class FakeComparisonRunExecutor : IComparisonRunExecutor
    {
        public Func<ComparisonRun, IRunProgressReporter, CancellationToken, Task<RunResultSummary>> ExecuteAsyncCore { get; init; } =
            (_, _, _) => Task.FromResult(CreateSummary());

        public Task<RunResultSummary> ExecuteAsync(
            ComparisonRun run,
            IRunProgressReporter progressReporter,
            CancellationToken cancellationToken = default) =>
            ExecuteAsyncCore(run, progressReporter, cancellationToken);
    }

    private sealed class FakeRunEventPublisher : IRunEventPublisher
    {
        public List<RunEvent> PublishedEvents { get; } = new List<RunEvent>();

        public List<RunStatus> PublishedStatuses => PublishedEvents.Select(runEvent => runEvent.Status).ToList();

        public Task PublishAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            PublishedEvents.Add(runEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeRunIdGenerator : IRunIdGenerator
    {
        private readonly RunId runId;

        public FakeRunIdGenerator(RunId runId)
        {
            this.runId = runId;
        }

        public RunId CreateId() => runId;
    }

    private sealed class FakeRunCancellationRegistry : IRunCancellationRegistry
    {
        private readonly Dictionary<RunId, CancellationTokenSource> sources = new Dictionary<RunId, CancellationTokenSource>();

        public List<RunId> RequestedRuns { get; } = new List<RunId>();

        public List<RunId> CompletedRuns { get; } = new List<RunId>();

        public CancellationToken CreateLinkedToken(RunId runId, CancellationToken cancellationToken)
        {
            CancellationTokenSource source = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            sources[runId] = source;
            return source.Token;
        }

        public bool RequestCancellation(RunId runId)
        {
            RequestedRuns.Add(runId);
            if (!sources.TryGetValue(runId, out CancellationTokenSource? source))
            {
                return false;
            }

            source.Cancel();
            return true;
        }

        public bool IsCancellationRequested(RunId runId) =>
            sources.TryGetValue(runId, out CancellationTokenSource? source) && source.IsCancellationRequested;

        public void Complete(RunId runId)
        {
            CompletedRuns.Add(runId);
            if (sources.Remove(runId, out CancellationTokenSource? source))
            {
                source.Dispose();
            }
        }
    }
}
