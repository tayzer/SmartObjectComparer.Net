using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Observability;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class ObservabilityFitnessTests
{
    [TestMethod]
    public void Recorder_WhenDiagnosticsPersistenceIsConfigured_CapturesBoundedSlowPathsAndExceptions()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ParityBench:Observability:LogDurations"] = "true",
                ["ParityBench:Observability:LogExceptions"] = "true",
                ["ParityBench:Observability:PersistDiagnostics"] = "true",
                ["ParityBench:Observability:SlowPathThresholdMs"] = "0",
                ["ParityBench:Observability:MaxSlowPathEntries"] = "2",
                ["ParityBench:Observability:MaxExceptionEntries"] = "1",
                ["ParityBench:Observability:EnableDetailedCompareTiming"] = "true",
            })
            .Build();
        ServiceCollection services = new ServiceCollection();
        services.AddLogging();
        services.AddParityBenchObservability(configuration);
        using ServiceProvider provider = services.BuildServiceProvider();
        IObservabilityRecorder recorder = provider.GetRequiredService<IObservabilityRecorder>();
        RunId runId = new RunId("run-1");

        recorder.RecordRequestPath(runId, "fast.json", TimeSpan.FromMilliseconds(1));
        recorder.RecordRequestPath(runId, "slow.json", TimeSpan.FromMilliseconds(50));
        recorder.RecordRequestPath(runId, "slowest.json", TimeSpan.FromMilliseconds(100));
        recorder.RecordException(runId, "EndpointExecution", new InvalidOperationException("first"), "one.json", EndpointSlot.A);
        recorder.RecordException(runId, "Comparison", new InvalidOperationException("second"), "two.json", EndpointSlot.B);
        RunDiagnosticsSnapshot? snapshot = recorder.CreateSnapshot(runId);

        Assert.IsTrue(recorder.IsDurationLoggingEnabled);
        Assert.IsTrue(recorder.IsExceptionLoggingEnabled);
        Assert.IsTrue(recorder.IsDiagnosticsPersistenceEnabled);
        Assert.IsTrue(recorder.IsDetailedCompareTimingEnabled);
        Assert.IsNotNull(snapshot);
        CollectionAssert.AreEqual(
            new[] { "slowest.json", "slow.json" },
            snapshot.SlowRequestPaths.Select(path => path.RelativePath).ToArray());
        Assert.AreEqual(1, snapshot.Exceptions.Count);
        Assert.AreEqual("EndpointExecution", snapshot.Exceptions[0].Stage);
    }
}
