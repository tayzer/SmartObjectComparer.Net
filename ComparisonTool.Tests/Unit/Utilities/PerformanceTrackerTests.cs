using System.IO;
using ComparisonTool.Core.Utilities;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Utilities;

[TestClass]
public class PerformanceTrackerTests
{
    [TestMethod]
    public void GetMetricsForScope_ShouldReturnOnlyScopedOperations_AndKeepOperationNamesReadable()
    {
        var tracker = new PerformanceTracker(NullLogger<PerformanceTracker>.Instance);

        using (tracker.BeginScope("run-a"))
        {
            tracker.TrackOperation("CompareDirectoriesAsync", static () => { });
            tracker.TrackOperation("SharedOperation", static () => { });
        }

        using (tracker.BeginScope("run-b"))
        {
            tracker.TrackOperation("SharedOperation", static () => { });
        }

        tracker.TrackOperation("UnscopedOperation", static () => { });

        var scopedMetrics = tracker.GetMetricsForScope("run-a");
        var globalMetrics = tracker.GetMetrics();

        scopedMetrics.Keys.Should().Contain(new[] { "CompareDirectoriesAsync", "SharedOperation" });
        scopedMetrics.Keys.Should().NotContain(new[] { "run-a", "run-b", "UnscopedOperation" });
        scopedMetrics["SharedOperation"].CallCount.Should().Be(1);

        globalMetrics["SharedOperation"].CallCount.Should().Be(2);
        globalMetrics.Should().ContainKey("UnscopedOperation");
    }

    [TestMethod]
    public void SaveReportToFileForScope_ShouldOnlyIncludeThatScope()
    {
        var tracker = new PerformanceTracker(NullLogger<PerformanceTracker>.Instance);
        var tempDirectory = Path.Combine(Path.GetTempPath(), "ComparisonToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);

        try
        {
            using (tracker.BeginScope("run-a"))
            {
                tracker.TrackOperation("ScopedOperation", static () => { });
            }

            using (tracker.BeginScope("run-b"))
            {
                tracker.TrackOperation("OtherScopedOperation", static () => { });
            }

            tracker.TrackOperation("UnscopedOperation", static () => { });

            var reportPath = Path.Combine(tempDirectory, "scoped-report.txt");
            tracker.SaveReportToFileForScope("run-a", reportPath);

            var reportContents = File.ReadAllText(reportPath);

            reportContents.Should().Contain("ScopedOperation");
            reportContents.Should().NotContain("OtherScopedOperation");
            reportContents.Should().NotContain("UnscopedOperation");
            reportContents.Should().NotContain("run-a::");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }
}