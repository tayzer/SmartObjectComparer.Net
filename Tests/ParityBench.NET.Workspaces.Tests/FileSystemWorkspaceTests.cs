using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
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
    public async Task OpenReadAsync_WhenArtifactExists_ReturnsSavedContent()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunArtifactStore store = new FileSystemRunArtifactStore(workspaceRoot);
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("hello"));
        ResponseArtifactMetadata metadata = await store.SaveResponseAsync(
            new RunId("run-1"),
            EndpointSlot.A,
            new RequestItem("one.json", "application/json", 5),
            200,
            "application/json",
            stream);

        await using Stream loadedStream = await store.OpenReadAsync(metadata.Artifact);
        using StreamReader reader = new StreamReader(loadedStream, Encoding.UTF8);
        string loadedContent = await reader.ReadToEndAsync();

        Assert.AreEqual("hello", loadedContent);
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
                RequestPairOutcome.Different,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.json"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.json"),
                areEqual: false,
                differenceCount: 1,
                differences: new[] { new ComparisonDifference("Name", "A", "B", "Changed") }),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(1, loadedDetails.Count);
        Assert.AreEqual(RequestPairOutcome.Different, loadedDetails[0].Outcome);
        Assert.AreEqual("runs/run-1/artifacts/A/one.json", loadedDetails[0].ResponseA?.Artifact.ArtifactId);
        Assert.AreEqual(1, loadedDetails[0].DifferenceCount);
        Assert.AreEqual("Name", loadedDetails[0].Differences[0].PropertyPath);
        Assert.AreEqual("A", loadedDetails[0].Differences[0].ValueA);
        Assert.AreEqual("B", loadedDetails[0].Differences[0].ValueB);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenOutcomeMessageExists_CanLoadIt()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.json",
                RequestPairOutcome.StatusCodeMismatch,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.json"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.json"),
                outcomeMessage: "Endpoint A returned 200 and endpoint B returned 500."),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual("Endpoint A returned 200 and endpoint B returned 500.", loadedDetails[0].OutcomeMessage);
    }

    [TestMethod]
    public async Task SaveRunDetails_WhenRawTextDifferencesExist_CanLoadThem()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = new[]
        {
            new RequestPairResult(
                "one.txt",
                RequestPairOutcome.BothNonSuccess,
                CreateResponse(EndpointSlot.A, "runs/run-1/artifacts/A/one.txt"),
                CreateResponse(EndpointSlot.B, "runs/run-1/artifacts/B/one.txt"),
                differenceCount: 1,
                differences: new[] { new ComparisonDifference("Body.Line[1]", "first", "second", "Raw line differs.") }),
        };

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, loadedDetails[0].Outcome);
        Assert.AreEqual("Body.Line[1]", loadedDetails[0].Differences[0].PropertyPath);
        Assert.AreEqual("Raw line differs.", loadedDetails[0].Differences[0].Message);
    }
    [TestMethod]
    public async Task SaveRunDetails_WhenManyDetailsAreSaved_WritesLoadableDetailIndex()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunDetailStore store = new FileSystemRunDetailStore(workspaceRoot);
        RequestPairResult[] details = Enumerable
            .Range(1, 5000)
            .Select(index => new RequestPairResult(
                $"request-{index}.json",
                RequestPairOutcome.Equal,
                CreateResponse(EndpointSlot.A, $"runs/run-1/artifacts/A/request-{index}.json"),
                CreateResponse(EndpointSlot.B, $"runs/run-1/artifacts/B/request-{index}.json")))
            .ToArray();

        RunDetailReference reference = await store.SaveDetailsAsync(new RunId("run-1"), details);
        IReadOnlyList<RequestPairResult> loadedDetails = await store.LoadDetailsAsync(reference);

        Assert.AreEqual(5000, loadedDetails.Count);
        Assert.AreEqual("request-1.json", loadedDetails[0].RelativePath);
        Assert.AreEqual("request-5000.json", loadedDetails[^1].RelativePath);
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
            detailIndexReference: new RunDetailReference("runs/run-1/details/index.json"),
            executionMetrics: new RunExecutionMetrics(
                TimeSpan.FromMilliseconds(10),
                TimeSpan.FromMilliseconds(5),
                TimeSpan.FromMilliseconds(2),
                TimeSpan.FromMilliseconds(3),
                requestCount: 1,
                maxConcurrency: 2,
                responseBytesWritten: 4));
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
        Assert.AreEqual(1, loadedSummary.ExecutionMetrics?.RequestCount);
        Assert.AreEqual(4, loadedSummary.ExecutionMetrics?.ResponseBytesWritten);
    }

    [TestMethod]
    public async Task SaveRun_WhenRunIncludesOptions_PersistsAndLoadsOptions()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemRunStore store = new FileSystemRunStore(workspaceRoot);
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            ignoreCollectionOrder: true,
            ignoreStringCase: true,
            ignoreTrailingWhitespaceAtEnd: true,
            treatNullAndEmptyCollectionsAsEqual: true,
            ignoreXmlNamespaces: false,
            maxDifferences: 12,
            ignoreRules: new[] { new IgnoreRuleDefinition("Name") },
            smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Id") },
            maskRules: new[] { new MaskRuleDefinition("Token", 4, "#") });
        RunOptions options = CreateOptions(comparisonOptions, new RequestExecutionOptions("application/xml"), new ContractProfileSelection("profile-a"));
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), options);

        await store.SaveAsync(run);
        ComparisonRun? loadedRun = await store.LoadAsync(run.Id);

        Assert.IsNotNull(loadedRun);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreCollectionOrder);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreStringCase);
        Assert.IsTrue(loadedRun.Options.Comparison.IgnoreTrailingWhitespaceAtEnd);
        Assert.IsTrue(loadedRun.Options.Comparison.TreatNullAndEmptyCollectionsAsEqual);
        Assert.IsFalse(loadedRun.Options.Comparison.IgnoreXmlNamespaces);
        Assert.AreEqual(12, loadedRun.Options.Comparison.MaxDifferences);
        Assert.AreEqual("Name", loadedRun.Options.Comparison.IgnoreRules[0].PropertyPath);
        Assert.AreEqual("Id", loadedRun.Options.Comparison.SmartIgnoreRules[0].Value);
        Assert.AreEqual("Token", loadedRun.Options.Comparison.MaskRules[0].PropertyPath);
        Assert.AreEqual("application/xml", loadedRun.Options.RequestExecution.ContentTypeOverride);
        Assert.AreEqual("profile-a", loadedRun.Options.ContractProfile?.ProfileId);
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

    private static RunOptions CreateOptions(
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        ContractProfileSelection? contractProfileSelection = null) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            comparisonOptions: comparisonOptions,
            requestExecutionOptions: requestExecutionOptions,
            contractProfileSelection: contractProfileSelection);

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
}


