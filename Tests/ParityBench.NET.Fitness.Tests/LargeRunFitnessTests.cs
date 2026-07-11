using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class LargeRunFitnessTests
{
    private readonly List<string> tempDirectories = new List<string>();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string tempDirectory in tempDirectories)
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task StageDirectory_WhenBatchExceedsLargeRunThreshold_PreservesSortedManifestAndSkipsNonRequests()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        const int requestCount = 1001;
        for (int index = requestCount; index >= 1; index--)
        {
            string relativePath = index % 2 == 0
                ? Path.Combine("nested", $"request-{index:D4}.json")
                : $"request-{index:D4}.xml";
            string path = Path.Combine(sourceRoot, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, $"payload-{index:D4}").ConfigureAwait(false);
        }

        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "request-0001.json.headers.json"), "{}").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "_ignored.json"), "{}").ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "ignored.bin"), "not a request").ConfigureAwait(false);
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store
            .StageDirectoryAsync(sourceRoot, new RequestBatchReference("fitness-batch"))
            .ConfigureAwait(false);

        string[] actualPaths = manifest.Requests.Select(request => request.RelativePath).ToArray();
        string[] expectedPaths = Directory
            .EnumerateFiles(sourceRoot, "*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .Select(path => Path.GetRelativePath(sourceRoot, path).Replace('\\', '/'))
            .Where(path => !Path.GetFileName(path).StartsWith('_') && !path.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.AreEqual(requestCount, manifest.Requests.Count);
        CollectionAssert.AreEqual(expectedPaths, actualPaths);
    }

    [TestMethod]
    public async Task SaveDetails_WhenBatchSpansPages_WritesManifestSidecarsAndLoadsTargetedPages()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] results = Enumerable
            .Range(1, 601)
            .Select(index => CreateResult(index))
            .ToArray();

        RunDetailReference reference = await store
            .SaveDetailsAsync(new RunId("run-1"), results)
            .ConfigureAwait(false);
        RunDetailPage middlePage = await store
            .LoadPageAsync(reference, new RunDetailQuery(offset: 250, limit: 125))
            .ConfigureAwait(false);
        RunDetailPage filteredPage = await store
            .LoadPageAsync(reference, new RunDetailQuery(limit: 50, outcome: RequestPairOutcome.Different))
            .ConfigureAwait(false);

        Assert.AreEqual(2, reference.SchemaVersion);
        Assert.AreEqual(250, reference.PageSize);
        Assert.AreEqual(601, reference.TotalCount);
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, "runs", "run-1", "details", "manifest.json")));
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, "runs", "run-1", "details", "analysis.json")));
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, "runs", "run-1", "details", "difference-index.json")));
        Assert.AreEqual(3, Directory.GetFiles(Path.Combine(workspaceRoot, "runs", "run-1", "details", "pages"), "page-*.json").Length);
        Assert.AreEqual(125, middlePage.Items.Count);
        Assert.AreEqual("request-0251.json", middlePage.Items[0].RelativePath);
        Assert.IsTrue(filteredPage.Items.All(item => item.Outcome == RequestPairOutcome.Different));

        using JsonDocument manifest = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(workspaceRoot, "runs", "run-1", "details", "manifest.json")).ConfigureAwait(false));
        Assert.AreEqual("runs/run-1/details/analysis.json", manifest.RootElement.GetProperty("AnalysisPath").GetString());
        Assert.AreEqual("runs/run-1/details/difference-index.json", manifest.RootElement.GetProperty("DifferenceIndexPath").GetString());
    }

    private string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ParityBenchFitnessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        tempDirectories.Add(tempDirectory);
        return tempDirectory;
    }

    private static RequestPairResult CreateResult(int index) =>
        new RequestPairResult(
            $"request-{index:D4}.json",
            index % 10 == 0 ? RequestPairOutcome.Different : RequestPairOutcome.Equal,
            CreateResponse(EndpointSlot.A, index),
            CreateResponse(EndpointSlot.B, index),
            areEqual: index % 10 != 0,
            differenceCount: index % 10 == 0 ? 1 : 0,
            differences: index % 10 == 0
                ? new[] { new ComparisonDifference("Name", $"left-{index}", $"right-{index}", "Name differs.") }
                : Array.Empty<ComparisonDifference>());

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, int index) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference($"runs/run-1/artifacts/{endpoint}/request-{index:D4}.json", "application/json"),
            200,
            "application/json",
            2,
            $"sha-{index:D4}-{endpoint}");
}
