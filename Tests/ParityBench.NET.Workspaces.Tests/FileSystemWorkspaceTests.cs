using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemWorkspaceTests
{
    [TestMethod]
    public async Task StageDirectory_WhenDirectoryContainsEligibleFiles_PreservesSortedRelativePaths()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(sourceRoot, "nested"));
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "z.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "nested", "a.xml"), "<a />");
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageDirectoryAsync(sourceRoot, new RequestBatchReference("batch-1"));

        CollectionAssert.AreEqual(
            new[] { "nested/a.xml", "z.json" },
            manifest.Requests.Select(request => request.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task StageDirectory_WhenDirectoryContainsSidecarsAndUnderscoreFiles_ExcludesNonRequests()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json.headers.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "_skip.json"), "{}");
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "skip.bin"), "binary");
        FileSystemRequestBatchStore store = new FileSystemRequestBatchStore(workspaceRoot);

        RequestBatchManifest manifest = await store.StageDirectoryAsync(sourceRoot, new RequestBatchReference("batch-1"));

        Assert.AreEqual(1, manifest.Requests.Count);
        Assert.AreEqual("one.json", manifest.Requests[0].RelativePath);
    }

    [TestMethod]
    public async Task SaveResponseArtifact_WhenStreamIsSaved_WritesContentAndReturnsHashMetadata()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunArtifactStore store = new FileSystemRunArtifactStore(workspaceRoot);
        byte[] content = Encoding.UTF8.GetBytes("hello");
        using MemoryStream stream = new MemoryStream(content);

        ResponseArtifactMetadata metadata = await store.SaveResponseAsync(
            new RunId("run-1"),
            EndpointSlot.A,
            new RequestItem("one.json", "application/json", content.Length),
            200,
            "application/json",
            stream);

        string artifactPath = Path.Combine(workspaceRoot, metadata.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(artifactPath));
        Assert.AreEqual("hello", await File.ReadAllTextAsync(artifactPath));
        Assert.AreEqual(content.Length, metadata.ContentLength);
        Assert.AreEqual(ToSha256(content), metadata.Sha256);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenDetailsAreSaved_CanLoadDetailIndexWithoutRawBodies()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.json",
                RequestPairOutcome.Equal,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.json"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.json")),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(1, loadedDetails.Count);
        Assert.AreEqual(RequestPairOutcome.Equal, loadedDetails[0].Outcome);
        Assert.AreEqual("runs/run-1/artifacts/A/one.json", loadedDetails[0].ResponseA?.Artifact.ArtifactId);
    }

    [TestMethod]
    public async Task SaveRun_WhenRunIsSaved_CanLoadRunAndSummary()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 1,
            equalPairs: 1,
            differentPairs: 0,
            errorPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/index.json"));
        ComparisonRun run = ComparisonRun
            .Create(new RunId("run-1"), CreateOptions())
            .Start()
            .Complete(summary);

        await store.SaveAsync(run);
        ComparisonRun? loadedRun = await store.LoadAsync(run.Id);
        RunResultSummary? loadedSummary = await store.LoadSummaryAsync(run.Id);

        Assert.IsNotNull(loadedRun);
        Assert.AreEqual(RunStatus.Completed, loadedRun.Status);
        Assert.IsNotNull(loadedSummary);
        Assert.AreEqual(1, loadedSummary.EqualPairs);
        Assert.AreEqual("runs/run-1/details/index.json", loadedSummary.DetailIndexReference?.DetailId);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
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

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}
