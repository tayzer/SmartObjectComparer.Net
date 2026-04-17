using System.IO;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

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

        new[] { "CompareDirectoriesAsync", "SharedOperation" }.All(scopedMetrics.ContainsKey).ShouldBeTrue();
        new[] { "run-a", "run-b", "UnscopedOperation" }.Any(scopedMetrics.ContainsKey).ShouldBeFalse();
        scopedMetrics["SharedOperation"].CallCount.ShouldBe(1);

        globalMetrics["SharedOperation"].CallCount.ShouldBe(2);
        globalMetrics.ContainsKey("UnscopedOperation").ShouldBeTrue();
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

            reportContents.ShouldContain("ScopedOperation");
            reportContents.ShouldNotContain("OtherScopedOperation");
            reportContents.ShouldNotContain("UnscopedOperation");
            reportContents.ShouldNotContain("run-a::");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void SaveReportToFileForScope_WhenSupplementalMetricsProvided_ShouldAppendSupplementalMetricsSection()
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

            var reportPath = Path.Combine(tempDirectory, "scoped-report-with-metrics.txt");
            tracker.SaveReportToFileForScope(
                "run-a",
                reportPath,
                [
                    new KeyValuePair<string, object?>("CollectionOrderDeterministicOrderingMs", 12L),
                    new KeyValuePair<string, object?>("CollectionOrderFallbackMs", 3L),
                    new KeyValuePair<string, object?>("CollectionOrderFallbackCount", 1),
                ]);

            var reportContents = File.ReadAllText(reportPath);

            reportContents.ShouldContain("Operation: ScopedOperation");
            Regex.IsMatch(reportContents, "(?s)Operation: ScopedOperation.*SUPPLEMENTAL METRICS").ShouldBeTrue();
            reportContents.ShouldContain("CollectionOrderDeterministicOrderingMs: 12");
            reportContents.ShouldContain("CollectionOrderFallbackMs: 3");
            reportContents.ShouldContain("CollectionOrderFallbackCount: 1");
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    [TestMethod]
    public void SaveReportToCsvForScope_WhenSupplementalMetricsProvided_ShouldAppendMetricValueSection()
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

            var reportPath = Path.Combine(tempDirectory, "scoped-report-with-metrics.csv");
            tracker.SaveReportToCsvForScope(
                "run-a",
                reportPath,
                [
                    new KeyValuePair<string, object?>("CollectionOrderDeterministicOrderingMs", 12L),
                    new KeyValuePair<string, object?>("CollectionOrderFallbackMs", 3L),
                    new KeyValuePair<string, object?>("CollectionOrderFallbackCount", 1),
                ]);

            var reportLines = File.ReadAllLines(reportPath);
            var blankLineIndex = Array.IndexOf(reportLines, string.Empty);

            reportLines[0].ShouldBe("Operation,CallCount,TotalTimeMs,AverageTimeMs,MedianTimeMs,MinTimeMs,MaxTimeMs");
            blankLineIndex.ShouldBeGreaterThan(0);
            reportLines[blankLineIndex + 1].ShouldBe("Metric,Value");
            reportLines.ShouldContain("CollectionOrderDeterministicOrderingMs,12");
            reportLines.ShouldContain("CollectionOrderFallbackMs,3");
            reportLines.ShouldContain("CollectionOrderFallbackCount,1");
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