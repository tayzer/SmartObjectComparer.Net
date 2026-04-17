using System.Text.Json;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Cli;

[TestClass]
public sealed class HtmlReportBundleBuilderTests
{
    private string tempDirectory = null!;

    [TestInitialize]
    public void Setup()
    {
        tempDirectory = Path.Combine(Path.GetTempPath(), "HtmlReportBundleBuilderTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(tempDirectory))
        {
            Directory.Delete(tempDirectory, true);
        }
    }

    [TestMethod]
    public void BuildSingleFile_RequestPairWithOversizedResponseFiles_OmitsDiffDocument()
    {
        var pair = CreateStructuredPair(
            file1Path: CreateFile("responseA.json", new string('A', 6 * 1024)),
            file2Path: CreateFile("responseB.json", new string('B', 6 * 1024)),
            propertyName: "Envelope.Body.Value",
            expected: "left",
            actual: "right");

        var context = CreateRequestContext(pair, HtmlReportMode.SingleFile);

        var diffDocument = HtmlReportBundleBuilder.BuildSingleFile(context)
            .Bootstrap
            .DetailChunks![0]
            .Pairs[0]
            .DiffDocument;

        diffDocument.ShouldBeNull();
    }

    [TestMethod]
    public void BuildSingleFile_RequestPairWithMissingResponseFiles_PreservesStructuredFallbackDiffDocument()
    {
        var pair = CreateStructuredPair(
            file1Path: Path.Combine(tempDirectory, "missing-a.json"),
            file2Path: Path.Combine(tempDirectory, "missing-b.json"),
            propertyName: "Envelope.Body.Value",
            expected: "left",
            actual: "right");

        var context = CreateRequestContext(pair, HtmlReportMode.SingleFile);

        var diffDocument = HtmlReportBundleBuilder.BuildSingleFile(context)
            .Bootstrap
            .DetailChunks![0]
            .Pairs[0]
            .DiffDocument;

        diffDocument.ShouldNotBeNull();
        diffDocument!.Format.ShouldBe("text");
        diffDocument.Lines.ShouldNotBeEmpty();
    }

    [TestMethod]
    public async Task WriteAsync_StaticSite_WithOversizedRequestResponseFiles_OmitsDiffDocumentFromChunk()
    {
        var pair = CreateStructuredPair(
            file1Path: CreateFile("responseA-large.json", new string('A', 6 * 1024)),
            file2Path: CreateFile("responseB-large.json", new string('B', 6 * 1024)),
            propertyName: "Envelope.Body.Value",
            expected: "left",
            actual: "right");

        var context = CreateRequestContext(pair, HtmlReportMode.StaticSite);
        var outputPath = Path.Combine(tempDirectory, "request-comparison-large.html");

        await HtmlReportWriter.WriteAsync(context, outputPath);

        var chunkPath = Path.Combine(tempDirectory, "request-comparison-large.data", "chunks", "pairs-0001.json");
        File.Exists(chunkPath).ShouldBeTrue();

        using var chunkDocument = JsonDocument.Parse(await File.ReadAllTextAsync(chunkPath));
        var pairElement = chunkDocument.RootElement.GetProperty("pairs")[0];
        pairElement.TryGetProperty("diffDocument", out _).ShouldBeFalse();
    }

    [TestMethod]
    public async Task WriteAsync_StaticSite_WritesHtmlAndJsonArtifacts()
    {
        var pair = CreateStructuredPair(
            file1Path: CreateFile("responseA-small.json", "{\"value\":\"left\"}"),
            file2Path: CreateFile("responseB-small.json", "{\"value\":\"right\"}"),
            propertyName: "value",
            expected: "left",
            actual: "right");

        var context = CreateRequestContext(pair, HtmlReportMode.StaticSite);
        var outputPath = Path.Combine(tempDirectory, "request-comparison.html");

        await HtmlReportWriter.WriteAsync(context, outputPath);

        File.Exists(outputPath).ShouldBeTrue();
        var html = await File.ReadAllTextAsync(outputPath);
        html.ShouldNotContain("__REPORT_DATA_JSON__");
        html.ShouldContain("\"mode\":\"static-site\"");
        html.ShouldContain("request-comparison.data/index.json");

        var indexPath = Path.Combine(tempDirectory, "request-comparison.data", "index.json");
        File.Exists(indexPath).ShouldBeTrue();

        using (var indexDocument = JsonDocument.Parse(await File.ReadAllTextAsync(indexPath)))
        {
            indexDocument.RootElement.GetProperty("totalPairs").GetInt32().ShouldBe(1);
            indexDocument.RootElement.GetProperty("pairs").GetArrayLength().ShouldBe(1);
        }

        var chunkPath = Path.Combine(tempDirectory, "request-comparison.data", "chunks", "pairs-0001.json");
        File.Exists(chunkPath).ShouldBeTrue();

        using var chunkDocument = JsonDocument.Parse(await File.ReadAllTextAsync(chunkPath));
        chunkDocument.RootElement.GetProperty("pairs").GetArrayLength().ShouldBe(1);
    }

    private static ReportContext CreateRequestContext(FilePairComparisonResult pair, HtmlReportMode htmlMode)
    {
        return new ReportContext
        {
            Result = new MultiFolderComparisonResult
            {
                AllEqual = false,
                TotalPairsCompared = 1,
                FilePairResults = new List<FilePairComparisonResult> { pair },
            },
            CommandName = "request",
            EndpointA = "https://endpoint-a.example",
            EndpointB = "https://endpoint-b.example",
            JobId = "job-123",
            HtmlMode = htmlMode,
        };
    }

    private FilePairComparisonResult CreateStructuredPair(
        string file1Path,
        string file2Path,
        string propertyName,
        string? expected,
        string? actual)
    {
        return new FilePairComparisonResult
        {
            File1Name = Path.GetFileName(file1Path),
            File2Name = Path.GetFileName(file2Path),
            File1Path = file1Path,
            File2Path = file2Path,
            RequestRelativePath = "requests/001.json",
            Result = new ComparisonResult(new ComparisonConfig())
            {
                Differences = new List<Difference>
                {
                    new()
                    {
                        PropertyName = propertyName,
                        Object1Value = expected,
                        Object2Value = actual,
                    },
                },
            },
        };
    }

    private string CreateFile(string fileName, string content)
    {
        var path = Path.Combine(tempDirectory, fileName);
        File.WriteAllText(path, content);
        return path;
    }
}