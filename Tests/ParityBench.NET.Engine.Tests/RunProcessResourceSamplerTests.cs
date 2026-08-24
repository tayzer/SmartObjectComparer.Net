using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class RunProcessResourceSamplerTests
{
    [TestMethod]
    public async Task StopAsync_UsesProcessDeltaAndCapturesPeaks()
    {
        FakeSource source = new(
            new ProcessResourceSample(TimeSpan.FromSeconds(10), 100, 200),
            new ProcessResourceSample(TimeSpan.FromSeconds(13), 300, 400),
            new ProcessResourceSample(TimeSpan.FromSeconds(14), 250, 350));
        await using RunProcessResourceSampler sampler = new(source, logicalProcessorCount: 4);

        sampler.Start();
        RunProcessResourceMetrics metrics = await sampler.StopAsync();

        Assert.AreEqual(TimeSpan.FromSeconds(4), metrics.ProcessCpuDuration);
        Assert.AreEqual(300, metrics.PeakWorkingSetBytes);
        Assert.AreEqual(400, metrics.PeakPrivateBytes);
        Assert.AreEqual(4, metrics.LogicalProcessorCount);
        Assert.IsTrue(metrics.AverageMachineCpuUtilizationPercent >= 0);
    }

    private sealed class FakeSource(params ProcessResourceSample[] samples) : IRunProcessMetricsSource
    {
        private readonly Queue<ProcessResourceSample> samples = new(samples);
        private ProcessResourceSample last;

        public ProcessResourceSample Capture()
        {
            if (samples.Count > 0) { last = samples.Dequeue(); }
            return last;
        }
    }
}
