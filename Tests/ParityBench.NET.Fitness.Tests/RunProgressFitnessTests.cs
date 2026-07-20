using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class RunProgressFitnessTests
{
    [TestMethod]
    public async Task StartRun_WhenStatusChangesBeforeThrottleWindow_PublishesLifecycleTransitions()
    {
        CapturingRunStore store = new CapturingRunStore();
        CapturingRunEventPublisher publisher = new CapturingRunEventPublisher();
        RunId runId = new RunId("run-1");
        RunOptions options = new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            largeRunOptions: new LargeRunOptions(progressUpdateItemInterval: 1000, progressUpdateMillisecondsInterval: 60000));
        await store.SaveAsync(ComparisonRun.Create(runId, options)).ConfigureAwait(false);
        ComparisonRunService service = new ComparisonRunService(
            store,
            new StatusChangingExecutor(),
            publisher,
            new FixedRunIdGenerator(runId),
            new InMemoryRunCancellationRegistry());

        await service.StartRunAsync(runId).ConfigureAwait(false);

        CollectionAssert.Contains(publisher.PublishedStatuses, RunStatus.Executing);
        CollectionAssert.Contains(publisher.PublishedStatuses, RunStatus.Parsing);
        CollectionAssert.Contains(publisher.PublishedStatuses, RunStatus.Comparing);
        CollectionAssert.Contains(publisher.PublishedStatuses, RunStatus.Completed);
    }

    private static RunResultSummary CreateSummary() =>
        new RunResultSummary(totalPairs: 1, equalPairs: 1, differentPairs: 0, errorPairs: 0);

    private sealed class StatusChangingExecutor : IComparisonRunExecutor
    {
        public async Task<RunResultSummary> ExecuteAsync(
            ComparisonRun run,
            IRunProgressReporter progressReporter,
            CancellationToken cancellationToken = default)
        {
            await progressReporter
                .ReportAsync(RunStatus.Parsing, new RunProgress(5, "Parsing.", 0, 100), cancellationToken)
                .ConfigureAwait(false);
            await progressReporter
                .ReportAsync(RunStatus.Comparing, new RunProgress(50, "Comparing.", 1, 100), cancellationToken)
                .ConfigureAwait(false);
            return CreateSummary();
        }
    }

    private sealed class CapturingRunStore : IRunStore
    {
        private readonly Dictionary<RunId, ComparisonRun> runs = new Dictionary<RunId, ComparisonRun>();

        public Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
        {
            runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<ComparisonRun?> LoadAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run);
        }

        public Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunListItem>>(runs.Values.Select(RunListItem.FromRun).ToArray());

        public Task<RunResultSummary?> LoadSummaryAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run?.Summary);
        }
    }

    private sealed class CapturingRunEventPublisher : IRunEventPublisher
    {
        public List<RunStatus> PublishedStatuses { get; } = new List<RunStatus>();

        public Task PublishAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            PublishedStatuses.Add(runEvent.Status);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRunIdGenerator : IRunIdGenerator
    {
        private readonly RunId runId;

        public FixedRunIdGenerator(RunId runId)
        {
            this.runId = runId;
        }

        public RunId CreateId() => runId;
    }
}
