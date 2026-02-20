using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.RequestComparison;

[TestClass]
public class RawTextComparisonServiceTests
{
    private RawTextComparisonService service = null!;
    private string tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        var logger = new Mock<ILogger<RawTextComparisonService>>();
        service = new RawTextComparisonService(logger.Object);
        tempDir = Path.Combine(Path.GetTempPath(), "RawTextComparisonTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDir))
        {
            Directory.Delete(tempDir, true);
        }
    }

    private (string pathA, string pathB) CreateResponseFiles(string bodyA, string bodyB)
    {
        var pathA = Path.Combine(tempDir, "responseA.txt");
        var pathB = Path.Combine(tempDir, "responseB.txt");
        File.WriteAllText(pathA, bodyA);
        File.WriteAllText(pathB, bodyB);
        return (pathA, pathB);
    }

    private static ClassifiedExecutionResult CreateClassified(
        RequestPairOutcome outcome,
        int statusA,
        int statusB,
        string? responsePathA = null,
        string? responsePathB = null,
        bool success = true,
        string? error = null)
    {
        return new ClassifiedExecutionResult
        {
            Execution = new RequestExecutionResult
            {
                Request = new RequestFileInfo
                {
                    RelativePath = "test.xml",
                    FilePath = "/tmp/test.xml",
                    ContentType = "application/xml",
                },
                Success = success,
                StatusCodeA = statusA,
                StatusCodeB = statusB,
                ResponsePathA = responsePathA,
                ResponsePathB = responsePathB,
                ErrorMessage = error,
            },
            Outcome = outcome,
            OutcomeReason = success ? $"A={statusA}, B={statusB}" : $"Failed: {error}",
        };
    }

    // --- OneOrBothFailed tests ---

    [TestMethod]
    public async Task CompareRawAsync_OneOrBothFailed_ReturnsErrorResult()
    {
        var classified = CreateClassified(
            RequestPairOutcome.OneOrBothFailed, 0, 0,
            success: false, error: "Connection timed out");

        var result = await service.CompareRawAsync(classified);

        result.PairOutcome.Should().Be(RequestPairOutcome.OneOrBothFailed);
        result.ErrorMessage.Should().Contain("Connection timed out");
        result.ErrorType.Should().Be("HttpRequestException");
        result.RawTextDifferences.Should().BeEmpty();
    }

    // --- StatusCodeMismatch tests ---

    [TestMethod]
    public async Task CompareRawAsync_StatusCodeMismatch_IncludesStatusCodeDifference()
    {
        var (pathA, pathB) = CreateResponseFiles("OK", "Internal Server Error");
        var classified = CreateClassified(
            RequestPairOutcome.StatusCodeMismatch, 200, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.PairOutcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
        result.HttpStatusCodeA.Should().Be(200);
        result.HttpStatusCodeB.Should().Be(500);
        result.RawTextDifferences.Should().Contain(d => d.Type == RawTextDifferenceType.StatusCodeDifference);
    }

    [TestMethod]
    public async Task CompareRawAsync_StatusCodeMismatch_IncludesBodyDifferences()
    {
        var (pathA, pathB) = CreateResponseFiles("<response>ok</response>", "<error>not found</error>");
        var classified = CreateClassified(
            RequestPairOutcome.StatusCodeMismatch, 200, 404,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.RawTextDifferences.Should().HaveCountGreaterThanOrEqualTo(2); // status diff + body diff
        result.RawTextDifferences.Should().Contain(d => d.Type == RawTextDifferenceType.StatusCodeDifference);
        result.RawTextDifferences.Should().Contain(d =>
            d.Type == RawTextDifferenceType.Modified ||
            d.Type == RawTextDifferenceType.OnlyInA ||
            d.Type == RawTextDifferenceType.OnlyInB);
    }

    // --- BothNonSuccess tests ---

    [TestMethod]
    public async Task CompareRawAsync_BothNonSuccess_SameStatusCode_NoStatusDiff()
    {
        var (pathA, pathB) = CreateResponseFiles("<error>timeout</error>", "<error>timeout</error>");
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.PairOutcome.Should().Be(RequestPairOutcome.BothNonSuccess);
        result.RawTextDifferences.Should().NotContain(d => d.Type == RawTextDifferenceType.StatusCodeDifference);
    }

    [TestMethod]
    public async Task CompareRawAsync_BothNonSuccess_IdenticalBodies_CountsAsEqual()
    {
        var (pathA, pathB) = CreateResponseFiles("error body", "error body");
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.ErrorMessage.Should().BeNull();
        result.HasError.Should().BeFalse();
        result.Summary.Should().NotBeNull();
        result.AreEqual.Should().BeTrue();
    }

    [TestMethod]
    public async Task CompareRawAsync_BothNonSuccess_DifferentBodies_ShowsDiffs()
    {
        var (pathA, pathB) = CreateResponseFiles("<error>timeout A</error>", "<error>timeout B</error>");
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.AreEqual.Should().BeFalse();
        result.RawTextDifferences.Should().Contain(d =>
            d.Type == RawTextDifferenceType.Modified ||
            d.Type == RawTextDifferenceType.OnlyInA ||
            d.Type == RawTextDifferenceType.OnlyInB);
    }

    // --- Identical bodies ---

    [TestMethod]
    public async Task CompareRawAsync_IdenticalBodies_DifferentStatusCodes_OnlyStatusDiff()
    {
        var (pathA, pathB) = CreateResponseFiles("same body", "same body");
        var classified = CreateClassified(
            RequestPairOutcome.StatusCodeMismatch, 200, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        // Should have only the status code diff, no body diffs
        result.AreEqual.Should().BeFalse();
        result.RawTextDifferences.Should().HaveCount(1);
        result.RawTextDifferences[0].Type.Should().Be(RawTextDifferenceType.StatusCodeDifference);
    }

    // --- Missing/empty response files ---

    [TestMethod]
    public async Task CompareRawAsync_MissingResponseFiles_HandlesGracefully()
    {
        var classified = CreateClassified(
            RequestPairOutcome.StatusCodeMismatch, 200, 500,
            responsePathA: "/nonexistent/path/a.txt",
            responsePathB: "/nonexistent/path/b.txt");

        var result = await service.CompareRawAsync(classified);

        // Should not throw. Status code diff is still present.
        result.RawTextDifferences.Should().Contain(d => d.Type == RawTextDifferenceType.StatusCodeDifference);
    }

    [TestMethod]
    public async Task CompareRawAsync_EmptyResponseFiles_HandlesGracefully()
    {
        var (pathA, pathB) = CreateResponseFiles("", "");
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        // Empty bodies are identical (after the empty string split)
        result.Should().NotBeNull();
    }

    // --- Multi-line diff tests ---

    [TestMethod]
    public async Task CompareRawAsync_MultiLineAdded_DetectsOnlyInB()
    {
        var bodyA = "line1\nline2\nline3";
        var bodyB = "line1\nINSERTED\nline2\nline3";
        var (pathA, pathB) = CreateResponseFiles(bodyA, bodyB);
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.RawTextDifferences.Should().Contain(d =>
            d.Type == RawTextDifferenceType.OnlyInB && d.TextB == "INSERTED");
    }

    [TestMethod]
    public async Task CompareRawAsync_MultiLineRemoved_DetectsOnlyInA()
    {
        var bodyA = "line1\nREMOVED\nline2\nline3";
        var bodyB = "line1\nline2\nline3";
        var (pathA, pathB) = CreateResponseFiles(bodyA, bodyB);
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.RawTextDifferences.Should().Contain(d =>
            d.Type == RawTextDifferenceType.OnlyInA && d.TextA == "REMOVED");
    }

    [TestMethod]
    public async Task CompareRawAsync_ModifiedLine_DetectsModification()
    {
        var bodyA = "line1\noriginal\nline3";
        var bodyB = "line1\nchanged\nline3";
        var (pathA, pathB) = CreateResponseFiles(bodyA, bodyB);
        var classified = CreateClassified(
            RequestPairOutcome.BothNonSuccess, 500, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.RawTextDifferences.Should().Contain(d =>
            d.Type == RawTextDifferenceType.Modified &&
            d.TextA == "original" &&
            d.TextB == "changed");
    }

    // --- CompareAllRawAsync batch tests ---

    [TestMethod]
    public async Task CompareAllRawAsync_MultiplePairs_ReturnsResultsForAll()
    {
        var (pathA1, pathB1) = CreateResponseFiles("body1a", "body1b");
        var pathA2 = Path.Combine(tempDir, "resp2a.txt");
        var pathB2 = Path.Combine(tempDir, "resp2b.txt");
        File.WriteAllText(pathA2, "body2a");
        File.WriteAllText(pathB2, "body2b");

        var classified = new List<ClassifiedExecutionResult>
        {
            CreateClassified(RequestPairOutcome.StatusCodeMismatch, 200, 500,
                responsePathA: pathA1, responsePathB: pathB1),
            CreateClassified(RequestPairOutcome.BothNonSuccess, 404, 404,
                responsePathA: pathA2, responsePathB: pathB2),
        };

        var results = await service.CompareAllRawAsync(classified);

        results.Should().HaveCount(2);
        results[0].PairOutcome.Should().Be(RequestPairOutcome.StatusCodeMismatch);
        results[1].PairOutcome.Should().Be(RequestPairOutcome.BothNonSuccess);
    }

    [TestMethod]
    public async Task CompareAllRawAsync_EmptyList_ReturnsEmpty()
    {
        var results = await service.CompareAllRawAsync(Array.Empty<ClassifiedExecutionResult>());

        results.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareAllRawAsync_ReportsProgress()
    {
        var (pathA, pathB) = CreateResponseFiles("a", "b");
        var classified = new List<ClassifiedExecutionResult>
        {
            CreateClassified(RequestPairOutcome.BothNonSuccess, 500, 500,
                responsePathA: pathA, responsePathB: pathB),
        };

        var progressReports = new List<(int Completed, int Total, string Message)>();
        var progress = new Progress<(int Completed, int Total, string Message)>(
            report => progressReports.Add(report));

        var results = await service.CompareAllRawAsync(classified, progress);

        results.Should().HaveCount(1);
        // Progress reporting is async via SynchronizationContext, so we give it a moment
        await Task.Delay(100);
        progressReports.Should().NotBeEmpty();
    }

    // --- CancellationToken ---

    [TestMethod]
    public async Task CompareAllRawAsync_CancellationRequested_ThrowsCancellation()
    {
        var (pathA, pathB) = CreateResponseFiles("a", "b");
        var classified = new List<ClassifiedExecutionResult>
        {
            CreateClassified(RequestPairOutcome.BothNonSuccess, 500, 500,
                responsePathA: pathA, responsePathB: pathB),
        };

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CompareAllRawAsync(classified, cancellationToken: cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // --- File path properties ---

    [TestMethod]
    public async Task CompareRawAsync_SetsFileNames()
    {
        var (pathA, pathB) = CreateResponseFiles("a", "b");
        var classified = CreateClassified(
            RequestPairOutcome.StatusCodeMismatch, 200, 500,
            responsePathA: pathA, responsePathB: pathB);

        var result = await service.CompareRawAsync(classified);

        result.File1Name.Should().Be("test.xml");
        result.File2Name.Should().Be("test.xml");
    }

    // --- CompareFilesRawAsync tests ---

    private (string file1, string file2) CreatePlainFiles(string contentA, string contentB)
    {
        var file1 = Path.Combine(tempDir, $"fileA_{Guid.NewGuid():N}.xml");
        var file2 = Path.Combine(tempDir, $"fileB_{Guid.NewGuid():N}.xml");
        File.WriteAllText(file1, contentA);
        File.WriteAllText(file2, contentB);
        return (file1, file2);
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_IdenticalFiles_ReturnsNoDifferences()
    {
        var content = "<root><item>Hello</item></root>";
        var (file1, file2) = CreatePlainFiles(content, content);

        var diffs = await service.CompareFilesRawAsync(file1, file2);

        diffs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_DifferentContent_ReturnsDifferences()
    {
        var (file1, file2) = CreatePlainFiles(
            "<root><item>Old</item></root>",
            "<root><item>New</item></root>");

        var diffs = await service.CompareFilesRawAsync(file1, file2);

        diffs.Should().NotBeEmpty();
        diffs.Should().Contain(d => d.Type == RawTextDifferenceType.Modified);
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_BothPathsNull_ReturnsEmpty()
    {
        var diffs = await service.CompareFilesRawAsync(null, null);

        diffs.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_MissingFile_ReturnsAllAsAdded()
    {
        var (file1, _) = CreatePlainFiles("<root>content</root>", "");
        var missingPath = Path.Combine(tempDir, "nonexistent.xml");

        var diffs = await service.CompareFilesRawAsync(file1, missingPath);

        diffs.Should().NotBeEmpty();
        diffs.Should().Contain(d => d.Type == RawTextDifferenceType.OnlyInA);
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_LargeFile_AddsTruncationNotice()
    {
        // Create a file larger than 5 KB (MaxBodyBytes)
        var largeContent = new string('X', 6 * 1024);
        var (file1, file2) = CreatePlainFiles(largeContent, "small");

        var diffs = await service.CompareFilesRawAsync(file1, file2);

        diffs.Should().NotBeEmpty();
        diffs.Should().Contain(d => d.Description != null && d.Description.Contains("truncated"));
    }

    [TestMethod]
    public async Task CompareFilesRawAsync_RespectsCancel()
    {
        var (file1, file2) = CreatePlainFiles("a", "b");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = () => service.CompareFilesRawAsync(file1, file2, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
