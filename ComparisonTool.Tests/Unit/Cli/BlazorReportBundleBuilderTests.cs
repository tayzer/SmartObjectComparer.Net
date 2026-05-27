using System.Text.Json;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization.BlazorReport;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Cli;

[TestClass]
public sealed class BlazorReportBundleBuilderTests : IDisposable
{
    private readonly List<string> createdDirectories = new List<string>();

    public void Dispose()
    {
        foreach (var directory in this.createdDirectories)
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [TestMethod]
    public async Task BuildJsonAsync_WhenPairHasNoError_WritesBundledRawContentReference()
    {
        var fileA = this.CreateTempFile("response-a.txt", "expected response");
        var fileB = this.CreateTempFile("response-b.txt", "actual response");
        var sourcePair = new FilePairComparisonResult
        {
            File1Name = fileA.Name,
            File2Name = fileB.Name,
            File1Path = fileA.FullName,
            File2Path = fileB.FullName,
            Summary = new DifferenceSummary
            {
                AreEqual = false,
                TotalDifferenceCount = 1,
            },
            RawTextDifferences = new List<RawTextDifference>
            {
                new RawTextDifference
                {
                    Type = RawTextDifferenceType.Modified,
                    LineNumberA = 1,
                    LineNumberB = 1,
                    TextA = "expected response",
                    TextB = "actual response",
                    Description = "Response body differs.",
                },
            },
        };
        var context = CreateContext(
            sourcePair);

        var json = await BlazorReportBundleBuilder.BuildJsonAsync(context).ConfigureAwait(false);
        var report = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default);
        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Result);
        report.Result!.FilePairResults.Count.ShouldBe(1);
        var pair = report.Result!.FilePairResults[0];

        pair.HasEmbeddedRawContent.ShouldBeFalse();
        pair.EmbeddedRawContentA.ShouldBeNull();
        pair.EmbeddedRawContentB.ShouldBeNull();
        pair.EmbeddedRawContentTruncatedA.ShouldBeFalse();
        pair.EmbeddedRawContentTruncatedB.ShouldBeFalse();
        pair.BundledRawContentPath.ShouldBe(BlazorReportBundleBuilder.BuildBundledRawContentPath(sourcePair, 0));
        pair.RawTextDifferences!.Count.ShouldBe(1);
    }

    [TestMethod]
    public async Task BuildJsonAsync_WhenPairHasError_EmbedsRawContent()
    {
        var fileA = this.CreateTempFile("fault-a.txt", "fault-a");
        var fileB = this.CreateTempFile("fault-b.txt", "fault-b");
        var context = CreateContext(
            new FilePairComparisonResult
            {
                File1Name = fileA.Name,
                File2Name = fileB.Name,
                File1Path = fileA.FullName,
                File2Path = fileB.FullName,
                ErrorMessage = "Comparison failed.",
                ErrorType = "InvalidOperationException",
            });

        var json = await BlazorReportBundleBuilder.BuildJsonAsync(context).ConfigureAwait(false);
        var report = JsonSerializer.Deserialize<ReportBootstrapData>(json, BlazorReportSerializerOptions.Default);
        Assert.IsNotNull(report);
        Assert.IsNotNull(report.Result);
        report.Result!.FilePairResults.Count.ShouldBe(1);
        var pair = report.Result!.FilePairResults[0];

        pair.HasEmbeddedRawContent.ShouldBeTrue();
        pair.EmbeddedRawContentA.ShouldBe("fault-a");
        pair.EmbeddedRawContentB.ShouldBe("fault-b");
        pair.EmbeddedRawContentTruncatedA.ShouldBeFalse();
        pair.EmbeddedRawContentTruncatedB.ShouldBeFalse();
        pair.BundledRawContentPath.ShouldBeNull();
    }

    [TestMethod]
    public async Task BuildBundledRawContentDataAsync_WhenPairUsesNormalizedJsonArtifacts_FormatsJsonForDisplay()
    {
        var fileA = this.CreateTempFile("success-a.json", "{\"resultCode\":\"00\",\"sourceSystem\":\"endpoint-a\"}");
        var fileB = this.CreateTempFile("success-b.json", "{\"resultCode\":\"00\",\"sourceSystem\":\"endpoint-b\"}");
        var pair = new FilePairComparisonResult
        {
            File1Name = fileA.Name,
            File2Name = fileB.Name,
            File1Path = fileA.FullName,
            File2Path = fileB.FullName,
            ContentTypeA = "application/json",
            ContentTypeB = "application/json",
        };

        var bundled = await BlazorReportBundleBuilder.BuildBundledRawContentDataAsync(pair).ConfigureAwait(false);

        bundled.ErrorMessage.ShouldBeNull();
        bundled.ContentA.ShouldContain("\n  \"resultCode\": \"00\"");
        bundled.ContentA.ShouldContain("\n  \"sourceSystem\": \"endpoint-a\"");
        bundled.ContentB.ShouldContain("\n  \"sourceSystem\": \"endpoint-b\"");
        bundled.IsTruncatedA.ShouldBeFalse();
        bundled.IsTruncatedB.ShouldBeFalse();
    }

    private static ReportContext CreateContext(FilePairComparisonResult pair)
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
        };
    }

    private FileInfo CreateTempFile(string fileName, string contents)
    {
        var directory = Path.Combine(Path.GetTempPath(), "ComparisonToolCliTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        this.createdDirectories.Add(directory);

        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, contents);

        return new FileInfo(path);
    }
}