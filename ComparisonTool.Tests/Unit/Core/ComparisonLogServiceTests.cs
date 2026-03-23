using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class ComparisonLogServiceTests
{
    [TestMethod]
    public async Task LogFilePairResult_WhenCalledConcurrently_ShouldAggregateSessionStatsSafely()
    {
        using var service = CreateService();
        var totalResults = 120;
        var sessionId = service.StartSession("TestModel", totalResults);
        var results = new List<FilePairComparisonResult>();

        results.AddRange(CreateResults(30, index => CreateEqualResult(index)));
        results.AddRange(CreateResults(40, index => CreateDifferentResult(index + 30)));
        results.AddRange(CreateResults(35, index => CreateErrorResult(index + 70, "NullReferenceException")));
        results.AddRange(CreateResults(15, index => CreateErrorResult(index + 105, "TaskCanceledException")));

        await InvokeConcurrentlyAsync(results, result => service.LogFilePairResult(sessionId, result));

        var stats = service.GetSessionStats(sessionId);

        stats.TotalFilePairs.Should().Be(totalResults);
        stats.ProcessedFilePairs.Should().Be(totalResults);
        stats.EqualFilePairs.Should().Be(30);
        stats.DifferentFilePairs.Should().Be(40);
        stats.ErrorFilePairs.Should().Be(50);
        stats.FilesWithErrors.Should().HaveCount(50);
        stats.ErrorsByType.Should().Contain(new KeyValuePair<string, int>("NullReferenceException", 35));
        stats.ErrorsByType.Should().Contain(new KeyValuePair<string, int>("TaskCanceledException", 15));
    }

    [TestMethod]
    public void GetSessionStats_ShouldReturnDetachedSnapshot()
    {
        using var service = CreateService();
        var sessionId = service.StartSession("TestModel", 3);

        service.LogFilePairResult(sessionId, CreateErrorResult(1, "NullReferenceException"));

        var firstSnapshot = service.GetSessionStats(sessionId);
        firstSnapshot.ErrorsByType["Injected"] = 99;
        firstSnapshot.FilesWithErrors.Add("Injected.xml vs Injected.xml");

        service.LogFilePairResult(sessionId, CreateDifferentResult(2));
        service.LogFilePairResult(sessionId, CreateErrorResult(3, "TaskCanceledException"));

        var secondSnapshot = service.GetSessionStats(sessionId);

        firstSnapshot.ProcessedFilePairs.Should().Be(1);
        firstSnapshot.ErrorFilePairs.Should().Be(1);
        firstSnapshot.ErrorsByType.Should().ContainKey("Injected");
        firstSnapshot.FilesWithErrors.Should().Contain("Injected.xml vs Injected.xml");

        secondSnapshot.ProcessedFilePairs.Should().Be(3);
        secondSnapshot.DifferentFilePairs.Should().Be(1);
        secondSnapshot.ErrorFilePairs.Should().Be(2);
        secondSnapshot.ErrorsByType.Should().Contain(new KeyValuePair<string, int>("NullReferenceException", 1));
        secondSnapshot.ErrorsByType.Should().Contain(new KeyValuePair<string, int>("TaskCanceledException", 1));
        secondSnapshot.ErrorsByType.Should().NotContainKey("Injected");
        secondSnapshot.FilesWithErrors.Should().NotContain("Injected.xml vs Injected.xml");
    }

    [TestMethod]
    public async Task GetSessionStats_WhenReadDuringConcurrentLogging_ShouldNotThrowAndShouldReturnConsistentSnapshots()
    {
        using var service = CreateService();
        const int totalResults = 150;
        var sessionId = service.StartSession("TestModel", totalResults);
        var results = CreateResults(totalResults, index => CreateErrorResult(index, "NullReferenceException"));
        using var stopReading = new CancellationTokenSource();

        var readerTask = Task.Run(async () =>
        {
            while (!stopReading.IsCancellationRequested)
            {
                var snapshot = service.GetSessionStats(sessionId);
                _ = snapshot.ErrorsByType.Sum(pair => pair.Value);
                _ = snapshot.FilesWithErrors.Count;
                await Task.Yield();
            }
        });

        await InvokeConcurrentlyAsync(results, result => service.LogFilePairResult(sessionId, result));
        stopReading.Cancel();
        await readerTask;

        var finalSnapshot = service.GetSessionStats(sessionId);
        finalSnapshot.ProcessedFilePairs.Should().Be(totalResults);
        finalSnapshot.ErrorFilePairs.Should().Be(totalResults);
        finalSnapshot.ErrorsByType.Should().Contain(new KeyValuePair<string, int>("NullReferenceException", totalResults));
        finalSnapshot.FilesWithErrors.Should().HaveCount(totalResults);
    }

    [TestMethod]
    public void LogFilePairResult_AfterSessionEnds_ShouldNotMutateFinalizedStats()
    {
        using var service = CreateService();
        var sessionId = service.StartSession("TestModel", 2);

        service.LogFilePairResult(sessionId, CreateDifferentResult(1));
        service.EndSession(sessionId, new MultiFolderComparisonResult());
        service.LogFilePairResult(sessionId, CreateErrorResult(2, "NullReferenceException"));

        var snapshot = service.GetSessionStats(sessionId);
        snapshot.ProcessedFilePairs.Should().Be(1);
        snapshot.DifferentFilePairs.Should().Be(1);
        snapshot.ErrorFilePairs.Should().Be(0);
        snapshot.ErrorsByType.Should().BeEmpty();
        snapshot.EndTime.Should().NotBeNull();
    }

    private static ComparisonLogService CreateService() => new(NullLogger<ComparisonLogService>.Instance);

    private static List<FilePairComparisonResult> CreateResults(int count, Func<int, FilePairComparisonResult> factory)
        => Enumerable.Range(0, count).Select(factory).ToList();

    private static async Task InvokeConcurrentlyAsync(
        IReadOnlyCollection<FilePairComparisonResult> results,
        Action<FilePairComparisonResult> action)
    {
        using var gate = new ManualResetEventSlim(false);
        var tasks = results
            .Select(result => Task.Run(() =>
            {
                gate.Wait();
                action(result);
            }))
            .ToArray();

        gate.Set();
        await Task.WhenAll(tasks);
    }

    private static FilePairComparisonResult CreateEqualResult(int index) => new()
    {
        File1Name = $"{index:D3}_Left.xml",
        File2Name = $"{index:D3}_Right.xml",
        Summary = new DifferenceSummary
        {
            AreEqual = true,
            TotalDifferenceCount = 0,
        },
    };

    private static FilePairComparisonResult CreateDifferentResult(int index) => new()
    {
        File1Name = $"{index:D3}_Left.xml",
        File2Name = $"{index:D3}_Right.xml",
        Summary = new DifferenceSummary
        {
            AreEqual = false,
            TotalDifferenceCount = 3,
        },
    };

    private static FilePairComparisonResult CreateErrorResult(int index, string errorType) => new()
    {
        File1Name = $"{index:D3}_Left.xml",
        File2Name = $"{index:D3}_Right.xml",
        ErrorType = errorType,
        ErrorMessage = "Object reference not set to an instance of an object.",
    };
}