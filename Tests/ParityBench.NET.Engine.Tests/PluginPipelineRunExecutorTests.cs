using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Tests;

/// <summary>
/// End-to-end coverage of the executor's plugin path: a run that selects a plugin
/// comparison, with plugin steps spliced into the built-in pipeline.
/// </summary>
[TestClass]
public sealed class PluginPipelineRunExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenPluginStepRewritesEndpointBRequest_SendsRewrittenBodyAndHeaders()
    {
        RequestItem request = new RequestItem(
            "one.json",
            "application/json",
            10,
            new Dictionary<string, string> { ["SOAPAction"] = "urn:common", ["X-Override"] = "request" },
            headersB: new Dictionary<string, string> { ["X-Endpoint-B"] = "request-b" });
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(_ => "{\"status\":\"OK\"}");
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(request),
            sender,
            new InMemoryArtifactStore(),
            new RewritingRequestStep());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        CapturedRequest endpointA = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.A);
        CapturedRequest endpointB = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.B);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual("urn:common", endpointA.Headers["SOAPAction"]);
        Assert.AreEqual("{\"source\":true}", endpointA.Body);
        Assert.AreEqual("alternate-request", endpointB.Body);
        Assert.AreEqual("application/vnd.alt+json", endpointB.ContentType);
        // The plugin step wins over the merged request headers, but headers it does
        // not touch still come through.
        Assert.AreEqual("profile", endpointB.Headers["X-Override"]);
        Assert.AreEqual("urn:profile", endpointB.Headers["SOAPAction"]);
        Assert.AreEqual("request-b", endpointB.Headers["X-Endpoint-B"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenPluginMappingStepThrows_ReturnsExecutionFailedPair()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(new RequestItem("one.json", "application/json", 10)),
            new CapturingEndpointRequestSender(_ => "{\"status\":\"OK\"}"),
            new InMemoryArtifactStore(),
            new ThrowingMappingStep());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenPluginStepFailsTheContext_ReturnsExecutionFailedPair()
    {
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(new RequestItem("one.json", "application/json", 10)),
            new CapturingEndpointRequestSender(_ => "{\"status\":\"OK\"}"),
            new InMemoryArtifactStore(),
            new ShortCircuitingRequestStep());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenResponsesAreLarge_PersistsRawArtifactsBeforeMapping()
    {
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        RecordingMappingStep mappingStep = new RecordingMappingStep(artifactStore);
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(endpointRequest =>
            "{\"status\":\"" + new string(endpointRequest.Endpoint == EndpointSlot.A ? 'a' : 'a', 128 * 1024) + "\"}");
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(new RequestItem("one.json", "application/json", 10)),
            sender,
            artifactStore,
            mappingStep);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        // Two raw responses plus the two canonical projections the mapping phase persisted.
        Assert.AreEqual(4, artifactStore.SaveCalls.Count);
        // Mapping read the persisted artifact rather than an in-memory response body.
        int expectedLength = "{\"status\":\"".Length + (128 * 1024) + "\"}".Length;
        CollectionAssert.AreEquivalent(
            new[] { expectedLength, expectedLength },
            mappingStep.SourceResponseLengths.ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenComparisonDefaultsIgnoreCollectionOrder_AppliesThemWithRunOptionsUnset()
    {
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(endpointRequest =>
            endpointRequest.Endpoint == EndpointSlot.A
                ? "{\"status\":\"OK\",\"items\":[\"one\",\"two\"]}"
                : "{\"status\":\"OK\",\"items\":[\"two\",\"one\"]}");
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(new RequestItem("one.json", "application/json", 10)),
            sender,
            new InMemoryArtifactStore(),
            pluginStep: null,
            defaults: new ComparisonRuleDefaults(ignoreCollectionOrder: true));

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(0, summary.DifferentPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenEndpointReturnsNonSuccess_ComparesRawResponsesInsteadOfMapping()
    {
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        RecordingMappingStep mappingStep = new RecordingMappingStep(artifactStore);
        ComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(new RequestItem("one.json", "application/json", 10)),
            new CapturingEndpointRequestSender(_ => "{\"status\":\"OK\"}", statusCode: 500),
            artifactStore,
            mappingStep);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        // A non-success pair says nothing about the comparison type, so mapping is
        // skipped and the raw responses are diffed instead.
        Assert.AreEqual(0, mappingStep.SourceResponseLengths.Count);
        Assert.AreEqual(1, summary.BothNonSuccessPairs);
        // Only the two raw responses were persisted: nothing was mapped, so no
        // canonical artifact exists for a pair that never reached the comparison type.
        Assert.AreEqual(2, artifactStore.SaveCalls.Count);
    }

    private static ComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        CapturingEndpointRequestSender sender,
        InMemoryArtifactStore artifactStore,
        IComparisonMiddleware? pluginStep,
        ComparisonRuleDefaults? defaults = null) =>
        new ComparisonRunExecutor(
            new FakeRequestBatchStore(manifest, "{\"source\":true}"),
            sender,
            artifactStore,
            new FakeRunDetailStore(),
            new HashOnlyResponseComparer(),
            new StubComparisonPlanFactory(
                new StubComparisonDefinition(defaults ?? new ComparisonRuleDefaults()),
                pluginStep is null ? Array.Empty<IComparisonMiddleware>() : new[] { pluginStep }),
            contractPayloadSerializer: new JsonXmlContractPayloadSerializer());

    private static RequestBatchManifest CreateManifest(RequestItem request) =>
        new RequestBatchManifest(new RequestBatchReference("batch-1"), new[] { request });

    private static ComparisonRun CreateRun() =>
        ComparisonRun.Create(
            new RunId("run-1"),
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://service-a.example.test")),
                new EndpointDefinition(new Uri("https://service-b.example.test")),
                TimeSpan.FromSeconds(30),
                2,
                pluginComparison: new PluginComparisonSelection("test.plugin", "test.plugin.comparison")))
            .Start();

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    public sealed class CanonicalResponse
    {
        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("items")]
        public List<string> Items { get; init; } = new List<string>();
    }

    private sealed class StubComparisonPlanFactory : IComparisonPlanFactory
    {
        private readonly IComparisonDefinition definition;
        private readonly IReadOnlyList<IComparisonMiddleware> steps;

        public StubComparisonPlanFactory(IComparisonDefinition definition, IReadOnlyList<IComparisonMiddleware> steps)
        {
            this.definition = definition;
            this.steps = steps;
        }

        public Task<ComparisonExecutionPlan?> CreateAsync(RunOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComparisonExecutionPlan?>(new ComparisonExecutionPlan(
                definition,
                steps,
                new PipelineConfiguration(),
                EmptyServiceProvider.Instance));
    }

    private sealed class StubComparisonDefinition : IComparisonDefinition<CanonicalResponse>
    {
        public StubComparisonDefinition(ComparisonRuleDefaults defaults) => DefaultComparisonRules = defaults;

        public string ComparisonId => "test.plugin.comparison";

        public string DisplayName => "Test comparison";

        public Type ComparisonType => typeof(CanonicalResponse);

        public ContractEndpointProfile EndpointA { get; } =
            new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

        public ContractEndpointProfile EndpointB { get; } =
            new ContractEndpointProfile(PayloadFormat.Json, "application/json", PayloadFormat.Json);

        public IReadOnlyList<string> DefaultStepIds { get; } = Array.Empty<string>();

        public IReadOnlyList<string> RequiredStepIds { get; } = Array.Empty<string>();

        public ComparisonRuleDefaults DefaultComparisonRules { get; }
    }

    private sealed class RewritingRequestStep : IEndpointComparisonMiddleware
    {
        public string StepId => "test.rewrite-request";

        public PipelinePhase Phase => PipelinePhase.Request;

        public int Order => 0;

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            if (context.Endpoint == EndpointSlot.B)
            {
                context.RequestBody = ContractPayload.FromBytes(
                    Encoding.UTF8.GetBytes("alternate-request"),
                    PayloadFormat.Json,
                    "application/vnd.alt+json");
                context.RequestHeaders["X-Override"] = "profile";
                context.RequestHeaders["SOAPAction"] = "urn:profile";
            }

            return next(cancellationToken);
        }
    }

    private sealed class ShortCircuitingRequestStep : IEndpointComparisonMiddleware
    {
        public string StepId => "test.short-circuit";

        public PipelinePhase Phase => PipelinePhase.Request;

        public int Order => 0;

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            context.Fail("Token exchange failed.");
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingMappingStep : IEndpointComparisonMiddleware
    {
        public string StepId => "test.throwing-mapping";

        public PipelinePhase Phase => PipelinePhase.Mapping;

        public int Order => 0;

        public ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Mapping is not supported for this response.");
    }

    private sealed class RecordingMappingStep : IEndpointComparisonMiddleware
    {
        private readonly IRunArtifactStore artifactStore;

        public RecordingMappingStep(IRunArtifactStore artifactStore) => this.artifactStore = artifactStore;

        public List<int> SourceResponseLengths { get; } = new List<int>();

        public string StepId => "test.recording-mapping";

        public PipelinePhase Phase => PipelinePhase.Mapping;

        public int Order => 0;

        public async ValueTask InvokeAsync(IEndpointPipelineContext context, PipelineDelegate next, CancellationToken cancellationToken)
        {
            await using Stream body = await artifactStore
                .OpenReadAsync(context.ResponseArtifact!.Artifact, cancellationToken)
                .ConfigureAwait(false);
            using StreamReader reader = new StreamReader(body);
            string content = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);

            lock (SourceResponseLengths)
            {
                SourceResponseLengths.Add(content.Length);
            }

            context.ComparisonInstance = new CanonicalResponse { Status = "OK" };
            await next(cancellationToken).ConfigureAwait(false);
        }
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
                CapturedRequests.Add(new CapturedRequest(
                    request.Endpoint,
                    body,
                    request.ContentType,
                    new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase)));
            }

            return new EndpointResponse(
                statusCode,
                "application/json",
                new MemoryStream(Encoding.UTF8.GetBytes(responseFactory(request))));
        }
    }

    private sealed record CapturedRequest(
        EndpointSlot Endpoint,
        string Body,
        string ContentType,
        IReadOnlyDictionary<string, string> Headers);

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

        public List<string> SaveCalls { get; } = new List<string>();

        public IReadOnlyDictionary<string, string> SavedBodies
        {
            get
            {
                lock (gate)
                {
                    return savedContent.ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetString(pair.Value), StringComparer.OrdinalIgnoreCase);
                }
            }
        }

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
                SaveCalls.Add(artifactId);
                savedContent[artifactId] = content;
            }

            return new ResponseArtifactMetadata(
                endpoint,
                new ArtifactReference(artifactId, contentType),
                statusCode,
                contentType,
                content.Length,
                ToSha256(content));
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

    private sealed class CapturingProgressReporter : IRunProgressReporter
    {
        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
