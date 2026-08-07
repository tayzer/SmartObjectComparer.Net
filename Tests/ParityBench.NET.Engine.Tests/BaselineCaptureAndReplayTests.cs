using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Workspaces;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Tests;

/// <summary>
/// Capture and replay through the real file-system baseline store: a version is
/// recorded from one endpoint, then replayed against a live one without the recorded
/// endpoint being available.
/// </summary>
[TestClass]
public sealed class BaselineCaptureAndReplayTests
{
    private const string MatchingResponse = "{\"status\":\"OK\",\"items\":[\"one\",\"two\"]}";
    private const string ChangedResponse = "{\"status\":\"OK\",\"items\":[\"one\",\"three\"]}";

    private string workspaceRoot = string.Empty;
    private FileSystemBaselineStore store = null!;

    [TestInitialize]
    public void Initialize()
    {
        workspaceRoot = Path.Combine(Path.GetTempPath(), "paritybench-baseline-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(workspaceRoot);
        store = new FileSystemBaselineStore(workspaceRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCapturingABaseline_CallsOnlyTheCapturedEndpointAndStoresEveryScenario()
    {
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(_ => MatchingResponse);
        ComparisonRunExecutor executor = CreateExecutor(CreateManifest("one.json", "two.json"), sender);

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun("capture-run", BaselineBinding.ForCapture("Orders v4")),
            new NoOpProgressReporter());

        // Only the captured side is called: the run has no second endpoint to compare against.
        Assert.AreEqual(2, sender.CapturedRequests.Count);
        Assert.IsTrue(sender.CapturedRequests.All(captured => captured.Endpoint == EndpointSlot.A));
        Assert.AreEqual(2, summary.EqualPairs);

        BaselinePackageManifest manifest = await LoadOnlyBaselineAsync();
        Assert.AreEqual("Orders v4", manifest.Name);
        Assert.AreEqual(1, manifest.Version);
        Assert.AreEqual("capture-run", manifest.CapturedFromRunId);
        CollectionAssert.AreEquivalent(
            new[] { "one.json", "two.json" },
            manifest.Scenarios.Select(scenario => scenario.RelativePath).ToArray());
        // Both the mapped model and the response it came from are kept: the model is
        // what a replay compares, the raw response is provenance.
        Assert.IsTrue(manifest.Scenarios.All(scenario => scenario.HasRawResponse));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenCapturingUnderAnExistingName_AddsANewVersionInsteadOfOverwriting()
    {
        await CaptureAsync("Orders v4", MatchingResponse);
        await CaptureAsync("Orders v4", ChangedResponse);

        IReadOnlyList<BaselineSummary> baselines = await store.ListAsync();

        Assert.AreEqual(2, baselines.Count);
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, baselines.Select(baseline => baseline.Version).ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenReplayingABaseline_NeverCallsTheCapturedEndpoint()
    {
        BaselinePackageManifest captured = await CaptureAsync("Orders v4", MatchingResponse);

        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(_ => MatchingResponse);
        ComparisonRunExecutor executor = CreateExecutor(CreateManifest("one.json"), sender);

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun("replay-run", BaselineBinding.ForReplay(captured.Id, captured.Version)),
            new NoOpProgressReporter());

        // The whole point of the feature: the recorded version is gone by now, so
        // nothing may be sent to it.
        Assert.AreEqual(1, sender.CapturedRequests.Count);
        Assert.AreEqual(EndpointSlot.B, sender.CapturedRequests[0].Endpoint);
        Assert.AreEqual(1, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenTheLiveResponseChanged_ReportsADifference()
    {
        BaselinePackageManifest captured = await CaptureAsync("Orders v4", MatchingResponse);

        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json"),
            new CapturingEndpointRequestSender(_ => ChangedResponse));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun("replay-run", BaselineBinding.ForReplay(captured.Id, captured.Version)),
            new NoOpProgressReporter());

        Assert.AreEqual(1, summary.DifferentPairs);
        Assert.AreEqual(0, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenAnIgnoreRuleCoversTheChangedField_ReplayStillReportsEqual()
    {
        BaselinePackageManifest captured = await CaptureAsync("Orders v4", MatchingResponse);

        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json"),
            new CapturingEndpointRequestSender(_ => ChangedResponse));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun(
                "replay-run",
                BaselineBinding.ForReplay(captured.Id, captured.Version),
                new ComparisonOptions(ignoreRules: new[] { new IgnoreRuleDefinition("Items", ignoreCompletely: true) })),
            new NoOpProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenTheBaselineHasNoScenarioForARequest_ReportsAnExecutionFailure()
    {
        BaselinePackageManifest captured = await CaptureAsync("Orders v4", MatchingResponse);

        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json", "unknown.json"),
            new CapturingEndpointRequestSender(_ => MatchingResponse));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun("replay-run", BaselineBinding.ForReplay(captured.Id, captured.Version)),
            new NoOpProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(1, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenTheCapturedEndpointReturnsNonSuccess_LeavesTheScenarioOutOfThePackage()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json"),
            new CapturingEndpointRequestSender(_ => "{\"error\":\"gone\"}", statusCode: 500));

        RunResultSummary summary = await executor.ExecuteAsync(
            CreateRun("capture-run", BaselineBinding.ForCapture("Orders v4")),
            new NoOpProgressReporter());

        // The failure is reported, but a response the endpoint could not produce must
        // not become someone's expected result.
        Assert.AreEqual(1, summary.BothNonSuccessPairs);
        BaselinePackageManifest manifest = await LoadOnlyBaselineAsync();
        Assert.AreEqual(0, manifest.Scenarios.Count);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenACaptureRunFails_LeavesNoBaselineBehind()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json"),
            new CapturingEndpointRequestSender(_ => MatchingResponse),
            detailStore: new ThrowingRunDetailStore());

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => executor.ExecuteAsync(
            CreateRun("capture-run", BaselineBinding.ForCapture("Orders v4")),
            new NoOpProgressReporter()));

        Assert.AreEqual(0, (await store.ListAsync()).Count);
    }

    private async Task<BaselinePackageManifest> CaptureAsync(string name, string response)
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest("one.json"),
            new CapturingEndpointRequestSender(_ => response));

        await executor.ExecuteAsync(
            CreateRun($"capture-{Guid.NewGuid():n}", BaselineBinding.ForCapture(name)),
            new NoOpProgressReporter());

        BaselineSummary summary = (await store.ListAsync())
            .Where(baseline => string.Equals(baseline.Name, name, StringComparison.Ordinal))
            .OrderByDescending(baseline => baseline.Version)
            .First();

        return await store.LoadManifestAsync(summary.Id, summary.Version)
            ?? throw new InvalidOperationException("Captured baseline could not be loaded.");
    }

    private async Task<BaselinePackageManifest> LoadOnlyBaselineAsync()
    {
        BaselineSummary summary = (await store.ListAsync()).Single();
        return await store.LoadManifestAsync(summary.Id, summary.Version)
            ?? throw new InvalidOperationException("Captured baseline could not be loaded.");
    }

    private ComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        CapturingEndpointRequestSender sender,
        IRunDetailStore? detailStore = null) =>
        new ComparisonRunExecutor(
            new FakeRequestBatchStore(manifest, "{\"source\":true}"),
            sender,
            new InMemoryArtifactStore(),
            detailStore ?? new FakeRunDetailStore(),
            new HashOnlyResponseComparer(),
            new StubComparisonPlanFactory(),
            observabilityRecorder: null,
            cleanupStage: null,
            contractPayloadSerializer: new JsonXmlContractPayloadSerializer(),
            baselineStore: store);

    private static RequestBatchManifest CreateManifest(params string[] relativePaths) =>
        new RequestBatchManifest(
            new RequestBatchReference("batch-1"),
            relativePaths.Select(path => new RequestItem(path, "application/json", 10)).ToArray());

    private static ComparisonRun CreateRun(
        string runId,
        BaselineBinding baseline,
        ComparisonOptions? comparisonOptions = null) =>
        ComparisonRun.Create(
            new RunId(runId),
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://service-a.example.test")),
                new EndpointDefinition(new Uri("https://service-b.example.test")),
                TimeSpan.FromSeconds(30),
                2,
                comparisonOptions: comparisonOptions,
                pluginComparison: new PluginComparisonSelection("test.plugin", "test.plugin.comparison", "1.2.3", environmentName: "test"),
                baseline: baseline))
            .Start();

    public sealed class CanonicalResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("items")]
        public List<string> Items { get; init; } = new List<string>();
    }

    private sealed class StubComparisonPlanFactory : IComparisonPlanFactory
    {
        public Task<ComparisonExecutionPlan?> CreateAsync(RunOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComparisonExecutionPlan?>(new ComparisonExecutionPlan(
                new StubComparisonDefinition(),
                Array.Empty<IComparisonMiddleware>(),
                new PipelineConfiguration(),
                EmptyServiceProvider.Instance));
    }

    private sealed class StubComparisonDefinition : IComparisonDefinition<CanonicalResponse>
    {
        public string ComparisonId => "test.plugin.comparison";

        public string DisplayName => "Test comparison";

        public Type ComparisonType => typeof(CanonicalResponse);

        public ContractEndpointProfile EndpointA { get; } =
            new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

        public ContractEndpointProfile EndpointB { get; } =
            new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

        public IReadOnlyList<string> DefaultStepIds { get; } = Array.Empty<string>();

        public IReadOnlyList<string> RequiredStepIds { get; } = Array.Empty<string>();

        public ComparisonRuleDefaults DefaultComparisonRules { get; } = new ComparisonRuleDefaults();
    }

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public static readonly EmptyServiceProvider Instance = new EmptyServiceProvider();

        public object? GetService(Type serviceType) => null;
    }

    private sealed class CapturingEndpointRequestSender : IEndpointRequestSender
    {
        private readonly Func<EndpointRequest, string> responseFactory;
        private readonly int statusCode;

        public CapturingEndpointRequestSender(Func<EndpointRequest, string> responseFactory, int statusCode = 200)
        {
            this.responseFactory = responseFactory;
            this.statusCode = statusCode;
        }

        public List<CapturedRequest> CapturedRequests { get; } = new List<CapturedRequest>();

        public async Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            lock (CapturedRequests)
            {
                CapturedRequests.Add(new CapturedRequest(request.Endpoint, body));
            }

            return new EndpointResponse(
                statusCode,
                "application/json",
                new MemoryStream(Encoding.UTF8.GetBytes(responseFactory(request))));
        }
    }

    private sealed record CapturedRequest(EndpointSlot Endpoint, string Body);

    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        private readonly RequestBatchManifest manifest;
        private readonly string body;

        public FakeRequestBatchStore(RequestBatchManifest manifest, string body)
        {
            this.manifest = manifest;
            this.body = body;
        }

        public Task<RequestBatchManifest> StageDirectoryAsync(
            string sourceDirectory,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> StageFilesAsync(
            string sourceDirectory,
            IReadOnlyList<string> sourceFiles,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> LoadManifestAsync(
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<Stream> OpenRequestBodyAsync(
            RequestBatchReference batchReference,
            RequestItem request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
    }

    private sealed class InMemoryArtifactStore : IRunArtifactStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, byte[]> savedContent = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public async Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default)
        {
            using MemoryStream memoryStream = new MemoryStream();
            await body.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            byte[] content = memoryStream.ToArray();
            string artifactId = $"runs/{runId.Value}/artifacts/{endpoint}/{request.RelativePath}";
            lock (gate)
            {
                savedContent[artifactId] = content;
            }

            return new ResponseArtifactMetadata(
                endpoint,
                new ArtifactReference(artifactId, contentType),
                statusCode,
                contentType,
                content.Length,
                Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
        }

        public Task<Stream> OpenReadAsync(ArtifactReference artifact, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<Stream>(new MemoryStream(savedContent[artifact.ArtifactId]));
            }
        }
    }

    private sealed class FakeRunDetailStore : IRunDetailStore
    {
        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailReference($"runs/{runId.Value}/details/index.json"));

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RequestPairResult>>(Array.Empty<RequestPairResult>());

        public Task<RunDetailPage> LoadPageAsync(
            RunDetailReference detailReference,
            RunDetailQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailPage(Array.Empty<RequestPairResult>(), 0, query.Offset, query.Limit));
    }

    /// <summary>Fails the run after scenarios have been captured, to prove the reserved version is dropped.</summary>
    private sealed class ThrowingRunDetailStore : IRunDetailStore
    {
        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Detail store is unavailable.");

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Detail store is unavailable.");

        public Task<RunDetailPage> LoadPageAsync(
            RunDetailReference detailReference,
            RunDetailQuery query,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Detail store is unavailable.");
    }

    private sealed class NoOpProgressReporter : IRunProgressReporter
    {
        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
