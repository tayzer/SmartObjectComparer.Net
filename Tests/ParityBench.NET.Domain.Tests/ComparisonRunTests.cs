using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Tests;

[TestClass]
public sealed class ComparisonRunTests
{
    [TestMethod]
    public void Create_WhenRunIdIsEmpty_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => ComparisonRun.Create(new RunId(string.Empty), CreateOptions()));
    }

    [TestMethod]
    public void Create_WhenOptionsAreValid_ReturnsCreatedRun()
    {
        RunId runId = new RunId("run-1");
        RunOptions options = CreateOptions();

        ComparisonRun run = ComparisonRun.Create(runId, options);

        Assert.AreEqual(runId, run.Id);
        Assert.AreEqual(options, run.Options);
        Assert.AreEqual(RunStatus.Created, run.Status);
        Assert.AreEqual(0, run.Progress.PercentComplete);
        Assert.IsFalse(run.IsTerminal);
    }

    [TestMethod]
    public void Advance_WhenProgressHasInvalidPercent_ThrowsArgumentOutOfRangeException()
    {
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start();

        AssertThrows<ArgumentOutOfRangeException>(() =>
            run.Advance(RunStatus.Executing, 101, "Too far."));
    }

    [TestMethod]
    public void Complete_WhenRunIsActive_ReturnsCompletedRunWithSummary()
    {
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start();
        RunResultSummary summary = CreateSummary();

        ComparisonRun completedRun = run.Complete(summary);

        Assert.AreEqual(RunStatus.Completed, completedRun.Status);
        Assert.AreEqual(100, completedRun.Progress.PercentComplete);
        Assert.AreEqual(summary, completedRun.Summary);
        Assert.IsTrue(completedRun.IsTerminal);
    }

    [TestMethod]
    public void Cancel_WhenRunIsTerminal_ThrowsInvalidRunStateException()
    {
        ComparisonRun completedRun = ComparisonRun
            .Create(new RunId("run-1"), CreateOptions())
            .Start()
            .Complete(CreateSummary());

        AssertThrows<InvalidRunStateException>(() => completedRun.Cancel());
    }

    [TestMethod]
    public void IsTerminal_WhenStatusIsCompletedFailedOrCancelled_ReturnsTrue()
    {
        ComparisonRun completedRun = ComparisonRun
            .Create(new RunId("completed"), CreateOptions())
            .Start()
            .Complete(CreateSummary());
        ComparisonRun failedRun = ComparisonRun
            .Create(new RunId("failed"), CreateOptions())
            .Start()
            .Fail("Execution failed.");
        ComparisonRun cancelledRun = ComparisonRun
            .Create(new RunId("cancelled"), CreateOptions())
            .Start()
            .Cancel();

        Assert.IsTrue(completedRun.IsTerminal);
        Assert.IsTrue(failedRun.IsTerminal);
        Assert.IsTrue(cancelledRun.IsTerminal);
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
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

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            8);

    private static RunResultSummary CreateSummary() =>
        new RunResultSummary(
            totalPairs: 3,
            equalPairs: 1,
            differentPairs: 1,
            errorPairs: 1,
            detailIndexReference: new RunDetailReference("details", new ArtifactReference("detail-index")));
}
