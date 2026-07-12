using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class BasicRequestAbFlowIntegrationTests
{
    [TestMethod]
    public async Task StartRun_WhenUsingRealWorkspacesAndEngine_CompletesAndPersistsSummaryArtifactsAndDetails()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json"), "{\"id\":1}");

        RequestBatchReference batchReference = new RequestBatchReference("batch-1");
        FileSystemRequestBatchStore requestBatchStore = new FileSystemRequestBatchStore(workspaceRoot);
        RequestBatchManifest manifest = await requestBatchStore.StageDirectoryAsync(sourceRoot, batchReference);

        FileSystemRunStore runStore = new FileSystemRunStore(workspaceRoot);
        FileSystemRunArtifactStore artifactStore = new FileSystemRunArtifactStore(workspaceRoot);
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        ComparisonRunExecutor executor = new ComparisonRunExecutor(
            requestBatchStore,
            new FixedEndpointRequestSender("response"),
            artifactStore,
            detailStore);
        ComparisonRunService service = new ComparisonRunService(
            runStore,
            executor,
            new CapturingRunEventPublisher(),
            new FixedRunIdGenerator(new RunId("run-1")),
            new InMemoryRunCancellationRegistry());

        ComparisonRun createdRun = await service.CreateRunAsync(CreateOptions(manifest.BatchReference));
        ComparisonRun completedRun = await service.StartRunAsync(createdRun.Id);
        RunResultSummary? loadedSummary = await service.LoadRunSummaryAsync(createdRun.Id);
        IReadOnlyList<RequestPairResult> details = await detailStore.LoadDetailsAsync(completedRun.Summary!.DetailIndexReference!);

        Assert.AreEqual(RunStatus.Completed, completedRun.Status);
        Assert.IsNotNull(loadedSummary);
        Assert.AreEqual(1, loadedSummary.EqualPairs);
        Assert.AreEqual(1, details.Count);
        Assert.AreEqual(RequestPairOutcome.Equal, details[0].Outcome);
        AssertArtifactExists(workspaceRoot, details[0].ResponseA);
        AssertArtifactExists(workspaceRoot, details[0].ResponseB);
    }

    [TestMethod]
    public async Task StartRun_WhenUsingModelComparerAndMasking_PersistsMaskedEqualDetails()
    {
        string workspaceRoot = CreateTempDirectory();
        string sourceRoot = CreateTempDirectory();
        await File.WriteAllTextAsync(Path.Combine(sourceRoot, "one.json"), "{\"id\":1}");

        RequestBatchReference batchReference = new RequestBatchReference("batch-1");
        FileSystemRequestBatchStore requestBatchStore = new FileSystemRequestBatchStore(workspaceRoot);
        RequestBatchManifest manifest = await requestBatchStore.StageDirectoryAsync(sourceRoot, batchReference);
        FileSystemRunStore runStore = new FileSystemRunStore(workspaceRoot);
        FileSystemRunArtifactStore artifactStore = new FileSystemRunArtifactStore(workspaceRoot);
        FileSystemRunDetailStore detailStore = new FileSystemRunDetailStore(workspaceRoot);
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<SampleResponse>("Sample");
        JsonXmlResponseBodyDeserializer deserializer = new JsonXmlResponseBodyDeserializer(registry);
        CompareNetObjectsResponseComparer comparer = new CompareNetObjectsResponseComparer(artifactStore, deserializer);
        ComparisonRunExecutor executor = new ComparisonRunExecutor(
            requestBatchStore,
            new EndpointSwitchingRequestSender(
                "{\"id\":1,\"name\":\"Alpha\",\"token\":\"secret-a\"}",
                "{\"id\":1,\"name\":\"alpha\",\"token\":\"secret-b\"}"),
            artifactStore,
            detailStore,
            comparer);
        ComparisonRunService service = new ComparisonRunService(
            runStore,
            executor,
            new CapturingRunEventPublisher(),
            new FixedRunIdGenerator(new RunId("run-1")),
            new InMemoryRunCancellationRegistry());
        ComparisonOptions comparisonOptions = new ComparisonOptions(
            ignoreStringCase: true,
            maskRules: new[] { new MaskRuleDefinition("token") });

        ComparisonRun createdRun = await service.CreateRunAsync(CreateOptions(manifest.BatchReference, "Sample", comparisonOptions));
        ComparisonRun completedRun = await service.StartRunAsync(createdRun.Id);
        IReadOnlyList<RequestPairResult> details = await detailStore.LoadDetailsAsync(completedRun.Summary!.DetailIndexReference!);
        string artifactA = await ReadArtifactAsync(workspaceRoot, details[0].ResponseA!);
        string artifactB = await ReadArtifactAsync(workspaceRoot, details[0].ResponseB!);

        Assert.AreEqual(RunStatus.Completed, completedRun.Status);
        Assert.AreEqual(1, completedRun.Summary!.EqualPairs);
        Assert.AreEqual(RequestPairOutcome.Equal, details[0].Outcome);
        Assert.AreEqual(0, details[0].DifferenceCount);
        Assert.IsFalse(artifactA.Contains("secret-a", StringComparison.Ordinal));
        Assert.IsFalse(artifactB.Contains("secret-b", StringComparison.Ordinal));
        Assert.IsTrue(artifactA.Contains("********", StringComparison.Ordinal));
        Assert.IsTrue(artifactB.Contains("********", StringComparison.Ordinal));
    }

    private static RunOptions CreateOptions(
        RequestBatchReference batchReference,
        string modelName = "Auto",
        ComparisonOptions? comparisonOptions = null) =>
        new RunOptions(
            batchReference,
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            modelName,
            comparisonOptions);

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void AssertArtifactExists(string workspaceRoot, ResponseArtifactMetadata? response)
    {
        Assert.IsNotNull(response);

        string artifactPath = Path.Combine(workspaceRoot, response.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar));
        Assert.IsTrue(File.Exists(artifactPath), $"Expected artifact file to exist: {artifactPath}");
    }

    private static async Task<string> ReadArtifactAsync(string workspaceRoot, ResponseArtifactMetadata response)
    {
        string artifactPath = Path.Combine(workspaceRoot, response.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar));
        return await File.ReadAllTextAsync(artifactPath);
    }

    private sealed class FixedEndpointRequestSender : IEndpointRequestSender
    {
        private readonly string responseBody;

        public FixedEndpointRequestSender(string responseBody)
        {
            this.responseBody = responseBody;
        }

        public Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(responseBody));
            return Task.FromResult(new EndpointResponse(200, "application/json", stream));
        }
    }

    private sealed class EndpointSwitchingRequestSender : IEndpointRequestSender
    {
        private readonly string endpointABody;
        private readonly string endpointBBody;

        public EndpointSwitchingRequestSender(string endpointABody, string endpointBBody)
        {
            this.endpointABody = endpointABody;
            this.endpointBBody = endpointBBody;
        }

        public Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            string body = request.Endpoint == EndpointSlot.A ? endpointABody : endpointBBody;
            Stream stream = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return Task.FromResult(new EndpointResponse(200, "application/json", stream));
        }
    }

    private sealed class CapturingRunEventPublisher : IRunEventPublisher
    {
        public List<RunEvent> Events { get; } = new List<RunEvent>();

        public Task PublishAsync(RunEvent runEvent, CancellationToken cancellationToken = default)
        {
            Events.Add(runEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FixedRunIdGenerator : IRunIdGenerator
    {
        private readonly RunId runId;

        public FixedRunIdGenerator(RunId runId)
        {
            this.runId = runId;
        }

        public RunId CreateId() => runId;
    }

    public sealed class SampleResponse
    {
        public int Id { get; set; }

        public string? Name { get; set; }

        public string? Token { get; set; }
    }
}

