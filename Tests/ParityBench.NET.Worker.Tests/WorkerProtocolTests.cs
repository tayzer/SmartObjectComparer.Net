using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Runs.Worker;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Worker.Tests;

[TestClass]
public sealed class WorkerProtocolTests
{
    [TestMethod]
    public void WorkerFrame_WhenSummaryHasMetrics_RoundTripsEveryField()
    {
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 10,
            equalPairs: 7,
            differentPairs: 2,
            errorPairs: 1,
            statusCodeMismatchPairs: 0,
            bothNonSuccessPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/manifest.json", pageSize: 100, totalCount: 10),
            executionMetrics: new RunExecutionMetrics(
                TimeSpan.FromSeconds(5),
                TimeSpan.FromSeconds(3),
                TimeSpan.FromSeconds(1),
                TimeSpan.FromSeconds(1),
                requestCount: 10,
                maxConcurrency: 4,
                responseBytesWritten: 2048));

        WorkerProtocol.WorkerFrame frame = new WorkerProtocol.WorkerFrame(
            WorkerProtocol.WorkerFrameKind.Summary,
            Summary: summary);

        string json = WorkerProtocol.Serialize(frame);
        WorkerProtocol.WorkerFrame? restored = WorkerProtocol.Deserialize<WorkerProtocol.WorkerFrame>(json);

        Assert.IsNotNull(restored);
        Assert.AreEqual(WorkerProtocol.WorkerFrameKind.Summary, restored.Kind);
        Assert.IsNotNull(restored.Summary);
        Assert.AreEqual(10, restored.Summary.TotalPairs);
        Assert.AreEqual(7, restored.Summary.EqualPairs);
        Assert.AreEqual("runs/run-1/details/manifest.json", restored.Summary.DetailIndexReference?.DetailId);
        Assert.AreEqual(2048, restored.Summary.ExecutionMetrics?.ResponseBytesWritten);
        // The serialized frame must be a single line so newline framing is unambiguous.
        Assert.IsFalse(json.Contains('\n', StringComparison.Ordinal));
    }

    [TestMethod]
    public void ProgressPayload_RoundTripsThroughRunProgress()
    {
        WorkerProtocol.ProgressPayload payload = WorkerProtocol.ProgressPayload.From(
            RunStatus.Executing,
            new RunProgress(42, "Processing", 42, 100),
            force: true);

        string json = WorkerProtocol.Serialize(new WorkerProtocol.WorkerFrame(
            WorkerProtocol.WorkerFrameKind.Progress,
            Progress: payload));
        WorkerProtocol.WorkerFrame restored = WorkerProtocol.Deserialize<WorkerProtocol.WorkerFrame>(json)!;

        Assert.AreEqual(RunStatus.Executing, restored.Progress!.Status);
        RunProgress progress = restored.Progress.ToProgress();
        Assert.AreEqual(42, progress.PercentComplete);
        Assert.AreEqual("Processing", progress.Message);
        Assert.AreEqual(100, progress.TotalItems);
        Assert.IsTrue(restored.Progress.Force);
    }
}
