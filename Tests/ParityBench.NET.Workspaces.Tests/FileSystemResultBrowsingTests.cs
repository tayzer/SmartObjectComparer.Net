using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemResultBrowsingTests
{
    [TestMethod]
    public async Task LoadDetailsPage_WhenIndexHasManyItems_ReturnsOnlyRequestedPage()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = Enumerable
            .Range(1, 20)
            .Select(index => CreateResult($"request-{index:00}.json", RequestPairOutcome.Equal))
            .ToArray();
        RunDetailReference reference = await detailStore.SaveDetailsAsync(new RunId("run-1"), details);

        RunDetailPage page = await detailStore.LoadPageAsync(reference, new RunDetailQuery(offset: 5, limit: 3));

        Assert.AreEqual(20, page.TotalCount);
        CollectionAssert.AreEqual(
            new[] { "request-06.json", "request-07.json", "request-08.json" },
            page.Items.Select(item => item.RelativePath).ToArray());
        Assert.IsTrue(page.HasMore);
    }

    [TestMethod]
    public async Task LoadDetailsPage_WhenSearchIsProvided_FiltersByRelativePath()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RunDetailReference reference = await detailStore.SaveDetailsAsync(
            new RunId("run-1"),
            new[]
            {
                CreateResult("customers/get.json", RequestPairOutcome.Equal),
                CreateResult("orders/get.json", RequestPairOutcome.Equal),
                CreateResult("customers/post.json", RequestPairOutcome.Different),
            });

        RunDetailPage page = await detailStore.LoadPageAsync(reference, new RunDetailQuery(limit: 10, relativePathSearch: "customers"));

        Assert.AreEqual(2, page.TotalCount);
        CollectionAssert.AreEqual(
            new[] { "customers/get.json", "customers/post.json" },
            page.Items.Select(item => item.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task LoadDetailsPage_WhenOutcomeFilterIsProvided_FiltersByOutcome()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RunDetailReference reference = await detailStore.SaveDetailsAsync(
            new RunId("run-1"),
            new[]
            {
                CreateResult("one.json", RequestPairOutcome.Equal),
                CreateResult("two.json", RequestPairOutcome.Different),
                CreateResult("three.json", RequestPairOutcome.StatusCodeMismatch),
            });

        RunDetailPage page = await detailStore.LoadPageAsync(
            reference,
            new RunDetailQuery(limit: 10, outcome: RequestPairOutcome.Different));

        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual("two.json", page.Items[0].RelativePath);
    }


    [TestMethod]
    public async Task IncrementalWriter_WhenResultsSpanPages_WritesPagedManifestAndReadsDirectPage()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        await using IRunDetailWriter writer = await detailStore.CreateWriterAsync(new RunId("run-1"), pageSize: 3);
        await writer.AppendAsync(Enumerable.Range(1, 8).Select(index => CreateResult($"request-{index:00}.json", RequestPairOutcome.Equal)).ToArray());
        RunDetailReference reference = await writer.CompleteAsync();

        Assert.AreEqual(2, reference.SchemaVersion);
        Assert.AreEqual(3, reference.PageSize);
        Assert.AreEqual(8, reference.TotalCount);
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, "runs", "run-1", "details", "manifest.json")));
        Assert.AreEqual(3, Directory.EnumerateFiles(Path.Combine(workspaceRoot, "runs", "run-1", "details", "pages"), "page-*.json").Count());

        RunDetailPage page = await detailStore.LoadPageAsync(reference, new RunDetailQuery(offset: 3, limit: 3));

        CollectionAssert.AreEqual(
            new[] { "request-04.json", "request-05.json", "request-06.json" },
            page.Items.Select(item => item.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task IncrementalWriter_WhenAppendedInManifestOrdinalOrder_PreservesDeterministicOrderAcrossPages()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] manifestOrdinalResults =
        {
            CreateResult("request-20.json", RequestPairOutcome.Equal),
            CreateResult("request-03.json", RequestPairOutcome.Equal),
            CreateResult("request-11.json", RequestPairOutcome.Equal),
            CreateResult("request-01.json", RequestPairOutcome.Equal),
        };

        await using IRunDetailWriter writer = await detailStore.CreateWriterAsync(new RunId("run-1"), pageSize: 2);
        await writer.AppendAsync(manifestOrdinalResults.Take(2).ToArray());
        await writer.AppendAsync(manifestOrdinalResults.Skip(2).ToArray());
        RunDetailReference reference = await writer.CompleteAsync();

        IReadOnlyList<RequestPairResult> loaded = await detailStore.LoadDetailsAsync(reference);

        CollectionAssert.AreEqual(
            manifestOrdinalResults.Select(result => result.RelativePath).ToArray(),
            loaded.Select(result => result.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task IncrementalWriter_WhenCompleted_PersistsAnalysisAndDifferenceIndexSidecars()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult different = new RequestPairResult(
            "request-01.json",
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, "request-01.json"),
            CreateResponse(EndpointSlot.B, "request-01.json"),
            differenceCount: 1,
            differences: new[] { new ParityBench.NET.Domain.Comparison.ComparisonDifference("Customer.Name", "A", "B", "Name differs.") });

        await using IRunDetailWriter writer = await detailStore.CreateWriterAsync(new RunId("run-1"), pageSize: 10);
        await writer.AppendAsync(new[] { different });
        RunDetailReference reference = await writer.CompleteAsync();

        Assert.IsNotNull(reference.AnalysisArtifact);
        Assert.IsNotNull(reference.DifferenceIndexArtifact);
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, reference.AnalysisArtifact!.ArtifactId.Replace('/', Path.DirectorySeparatorChar))));
        Assert.IsTrue(File.Exists(Path.Combine(workspaceRoot, reference.DifferenceIndexArtifact!.ArtifactId.Replace('/', Path.DirectorySeparatorChar))));

        var analysis = await detailStore.LoadAnalysisAsync(reference);
        var index = await detailStore.LoadDifferenceIndexAsync(reference);

        Assert.IsNotNull(analysis);
        Assert.AreEqual(1, analysis!.TotalPairs);
        Assert.AreEqual(1, analysis.TotalDifferences);
        Assert.IsNotNull(index);
        Assert.AreEqual(1, index!.TotalDifferences);
        Assert.AreEqual("Customer.Name", index.Properties[0].NormalizedPath);
    }
    [TestMethod]
    public async Task ReadArtifactPreview_WhenStreamIsOpened_DoesNotLoadDetailIndex()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunArtifactStore artifactStore = new FileSystemRunArtifactStore(workspaceRoot);
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        FileSystemRunStore runStore = new FileSystemRunStore(workspaceRoot);
        byte[] body = Encoding.UTF8.GetBytes("artifact body");
        ResponseArtifactMetadata metadata = await artifactStore.SaveResponseAsync(
            new RunId("run-1"),
            EndpointSlot.A,
            new RequestItem("one.txt", "text/plain", body.Length),
            200,
            "text/plain",
            new MemoryStream(body));
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 1,
            equalPairs: 1,
            differentPairs: 0,
            errorPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/missing-index.json"));
        await runStore.SaveAsync(ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start().Complete(summary));
        ComparisonRunResultService resultService = new ComparisonRunResultService(runStore, detailStore, artifactStore);

        ArtifactContentPreview preview = await resultService.ReadArtifactPreviewAsync(metadata.Artifact, maxBytes: 100);

        Assert.AreEqual("artifact body", preview.Content);
        Assert.IsFalse(preview.IsTruncated);
    }

    [TestMethod]
    public async Task LoadDetails_WhenRetentionStatesAreExplicit_PreservesRetainedTrimmedAndMissingLabels()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult item = new RequestPairResult(
            "one.json",
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, "one.json"),
            CreateResponse(EndpointSlot.B, "one.json"),
            areEqual: false,
            differenceCount: 1,
            artifactRetentionState: new PairArtifactRetentionState(
                ArtifactRetentionState.Retained,
                ArtifactRetentionState.TrimmedByPolicy,
                ArtifactRetentionState.TrimmedByPolicy,
                ArtifactRetentionState.Retained,
                ArtifactRetentionState.MissingUnexpectedly,
                ArtifactRetentionState.Retained),
            retentionAppliedAt: DateTimeOffset.Parse("2026-01-01T00:00:00Z"));

        RunDetailReference reference = await detailStore.SaveDetailsAsync(new RunId("run-1"), new[] { item });
        IReadOnlyList<RequestPairResult> loaded = await detailStore.LoadDetailsAsync(reference);

        Assert.AreEqual(1, loaded.Count);
        Assert.AreEqual(ArtifactRetentionState.Retained, loaded[0].ArtifactRetentionState.RawResponseA);
        Assert.AreEqual(ArtifactRetentionState.TrimmedByPolicy, loaded[0].ArtifactRetentionState.RawResponseB);
        Assert.AreEqual(ArtifactRetentionState.MissingUnexpectedly, loaded[0].ArtifactRetentionState.FocusedResponseA);
        Assert.IsNotNull(loaded[0].RetentionAppliedAt);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static RequestPairResult CreateResult(string relativePath, RequestPairOutcome outcome) =>
        new RequestPairResult(relativePath, outcome, CreateResponse(EndpointSlot.A, relativePath), CreateResponse(EndpointSlot.B, relativePath));

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string relativePath) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference($"runs/run-1/artifacts/{endpoint}/{relativePath}", "application/json"),
            200,
            "application/json",
            2,
            "abc");

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2);
}