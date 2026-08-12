using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class InterruptedRunRecoveryServiceTests
{
    [TestMethod]
    public async Task CancelNonTerminalRunsAsync_CancelsEveryPersistedActiveStatusAndPreservesTerminalRuns()
    {
        FakeRunUseCases runUseCases = new FakeRunUseCases(new[]
        {
            RunStatus.Created,
            RunStatus.Pending,
            RunStatus.Parsing,
            RunStatus.Executing,
            RunStatus.Comparing,
            RunStatus.Analyzing,
            RunStatus.Finalizing,
            RunStatus.Completed,
            RunStatus.Failed,
            RunStatus.Cancelled,
        });
        InterruptedRunRecoveryService service = new InterruptedRunRecoveryService(runUseCases);

        int cancelledCount = await service.CancelNonTerminalRunsAsync(InterruptedRunRecoveryService.StartupCancellationMessage);

        Assert.AreEqual(7, cancelledCount);
        CollectionAssert.AreEquivalent(
            new[] { RunStatus.Created, RunStatus.Pending, RunStatus.Parsing, RunStatus.Executing, RunStatus.Comparing, RunStatus.Analyzing, RunStatus.Finalizing },
            runUseCases.CancelledRuns.Select(run => run.Status).ToArray());
        Assert.IsTrue(runUseCases.CancelledRuns.All(run => run.Message == InterruptedRunRecoveryService.StartupCancellationMessage));
        CollectionAssert.DoesNotContain(runUseCases.CancelledRuns.Select(run => run.Id.Value).ToArray(), "run-7");
        CollectionAssert.DoesNotContain(runUseCases.CancelledRuns.Select(run => run.Id.Value).ToArray(), "run-8");
        CollectionAssert.DoesNotContain(runUseCases.CancelledRuns.Select(run => run.Id.Value).ToArray(), "run-9");
    }

    [TestMethod]
    public async Task CancelNonTerminalRunsAsync_AfterRecoveryIsIdempotent()
    {
        FakeRunUseCases runUseCases = new FakeRunUseCases(new[] { RunStatus.Executing, RunStatus.Completed });
        InterruptedRunRecoveryService service = new InterruptedRunRecoveryService(runUseCases);

        await service.CancelNonTerminalRunsAsync(InterruptedRunRecoveryService.SuspendCancellationMessage);
        int secondCancellationCount = await service.CancelNonTerminalRunsAsync(InterruptedRunRecoveryService.SuspendCancellationMessage);

        Assert.AreEqual(0, secondCancellationCount);
        Assert.AreEqual(1, runUseCases.CancelledRuns.Count);
    }

    [TestMethod]
    public async Task RecoverAsync_WhenSnapshotWarningsExist_ReturnsWarningsWithoutStoppingCancellation()
    {
        FakeRunUseCases runUseCases = new FakeRunUseCases(new[] { RunStatus.Executing });
        runUseCases.RecoveryWarnings.Add(new RunSnapshotRecoveryWarning(
            "C:/workspace/runs/bad/run.json",
            "C:/workspace/runs/bad/run.json.20260812.corrupt",
            "Malformed snapshot was quarantined."));
        InterruptedRunRecoveryService service = new InterruptedRunRecoveryService(runUseCases);

        InterruptedRunRecoveryResult result = await service.RecoverAsync(InterruptedRunRecoveryService.StartupCancellationMessage);

        Assert.AreEqual(1, result.CancelledRunCount);
        Assert.AreEqual(1, result.SnapshotWarnings.Count);
        Assert.IsTrue(result.HasWarnings);
        Assert.AreEqual(0, (await runUseCases.DrainRecoveryWarningsAsync()).Count);
    }

    private sealed class FakeRunUseCases : IComparisonRunUseCases
    {
        public FakeRunUseCases(IEnumerable<RunStatus> statuses)
        {
            Runs = statuses.Select((status, index) => new RecordedRun(new RunId($"run-{index}"), status)).ToList();
        }

        public List<RecordedRun> Runs { get; }

        public List<CancelledRun> CancelledRuns { get; } = new List<CancelledRun>();

        public List<RunSnapshotRecoveryWarning> RecoveryWarnings { get; } = new List<RunSnapshotRecoveryWarning>();

        public Task<ComparisonRun> CreateRunAsync(RunOptions options, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> StartRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> CancelRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            CancelRunAsync(runId, cancellationMessage: null, cancellationToken);

        public Task<ComparisonRun> CancelRunAsync(
            RunId runId,
            string? cancellationMessage,
            CancellationToken cancellationToken = default)
        {
            RecordedRun run = Runs.Single(run => run.Id == runId);
            CancelledRuns.Add(new CancelledRun(run.Id, run.Status, cancellationMessage));
            run.Status = RunStatus.Cancelled;
            run.Message = cancellationMessage;
            return Task.FromResult(ComparisonRun.Create(runId, CreateOptions()).Start().Cancel(cancellationMessage));
        }

        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunListItem>>(Runs.Select(run => new RunListItem(
                run.Id,
                run.Status,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow,
                new RunProgress(0, run.Status.ToString()))).ToList());

        public Task<IReadOnlyList<RunSnapshotRecoveryWarning>> DrainRecoveryWarningsAsync(
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RunSnapshotRecoveryWarning> warnings = RecoveryWarnings.ToList();
            RecoveryWarnings.Clear();
            return Task.FromResult(warnings);
        }

        public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RunResultSummary?>(null);

        private static RunOptions CreateOptions() =>
            new RunOptions(
                new RequestBatchReference("batch"),
                new EndpointDefinition(new Uri("https://a.example.test")),
                new EndpointDefinition(new Uri("https://b.example.test")),
                TimeSpan.FromSeconds(30),
                1);
    }

    private sealed class RecordedRun
    {
        public RecordedRun(RunId id, RunStatus status)
        {
            Id = id;
            Status = status;
        }

        public RunId Id { get; }

        public RunStatus Status { get; set; }

        public string? Message { get; set; }
    }

    private sealed record CancelledRun(RunId Id, RunStatus Status, string? Message);
}
