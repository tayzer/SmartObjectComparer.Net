using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class ContractProfileRunExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenContractProfileIsConfigured_TransformsEndpointBRequestAndAppliesProfileHeaders()
    {
        RequestItem request = new RequestItem(
            "one.xml",
            "application/xml",
            10,
            new Dictionary<string, string> { ["SOAPAction"] = "urn:common", ["X-Override"] = "request" },
            headersB: new Dictionary<string, string> { ["X-Endpoint-B"] = "request-b" });
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(_ => "<ok />");
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(request),
            sender,
            new FakeContractProfile());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        CapturedRequest endpointARequest = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.A);
        CapturedRequest endpointBRequest = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.B);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual("urn:common", endpointARequest.Headers["SOAPAction"]);
        Assert.AreEqual("urn:profile", endpointBRequest.Headers["SOAPAction"]);
        Assert.AreEqual("application/vnd.alt+json", endpointBRequest.ContentType);
        Assert.AreEqual("alternate-request", endpointBRequest.Body);
        Assert.AreEqual("profile", endpointBRequest.Headers["X-Override"]);
        Assert.AreEqual("request-b", endpointBRequest.Headers["X-Endpoint-B"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenSourceRequestFormatIsUnsupported_ReturnsExecutionFailedPair()
    {
        RequestItem request = new RequestItem("one.txt", "text/plain", 10);
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(request),
            new CapturingEndpointRequestSender(_ => "<ok />"),
            new FakeContractProfile());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenResponseNormalizationFails_ReturnsExecutionFailedPair()
    {
        RequestItem request = new RequestItem("one.xml", "application/xml", 10);
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateManifest(request),
            new CapturingEndpointRequestSender(_ => "<ok />"),
            new ThrowingNormalizationProfile());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
    }
    [TestMethod]
    public async Task ExecuteAsync_WhenContractProfileUsesLargeResponses_PersistsRawArtifactsBeforeNormalization()
    {
        RequestItem request = new RequestItem("one.xml", "application/xml", 10);
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        ReadingNormalizationProfile profile = new ReadingNormalizationProfile();
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(endpointRequest =>
            endpointRequest.Endpoint == EndpointSlot.A
                ? new string('a', 128 * 1024)
                : new string('b', 128 * 1024));
        BasicComparisonRunExecutor executor = new BasicComparisonRunExecutor(
            new FakeRequestBatchStore(CreateManifest(request), "<request />"),
            sender,
            artifactStore,
            new FakeRunDetailStore(),
            new HashOnlyResponseComparer(),
            CreateRegistry(profile));

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(4, artifactStore.SaveCalls.Count);
        CollectionAssert.AreEquivalent(
            new[] { 128 * 1024, 128 * 1024 },
            profile.SourceResponseLengths.ToArray());
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenUsingBuiltInSampleProfile_ComparesCanonicalArtifacts()
    {
        RequestItem request = new RequestItem("one.xml", "application/xml", 10);
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        IContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        IContractProfile sampleProfile = BuiltInContractProfiles.CreateSampleSoapToJson(serializer);
        ContractProfileRegistry registry = CreateRegistry(sampleProfile);
        ResponseModelRegistry modelRegistry = new ResponseModelRegistry();
        modelRegistry.Register<SampleSoapCustomerLookupResponseEnvelope>(BuiltInContractProfiles.SampleModelName);
        CompareNetObjectsResponseComparer comparer = new CompareNetObjectsResponseComparer(
            artifactStore,
            new JsonXmlResponseBodyDeserializer(modelRegistry));
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender(request =>
            request.Endpoint == EndpointSlot.A
                ? "<Envelope><Body><CustomerLookupResponse><StatusCode>OK</StatusCode><CustomerName>Ada</CustomerName><SensitiveToken>tok</SensitiveToken></CustomerLookupResponse></Body></Envelope>"
                : "{\"statusCode\":\"OK\",\"customerName\":\"Ada\",\"payload\":{\"raw_token\":\"tok\"}}");
        BasicComparisonRunExecutor executor = new BasicComparisonRunExecutor(
            new FakeRequestBatchStore(CreateManifest(request), "<Envelope><Body><CustomerLookupRequest><CustomerId>123</CustomerId><SensitiveToken>tok</SensitiveToken></CustomerLookupRequest></Body></Envelope>"),
            sender,
            artifactStore,
            new FakeRunDetailStore(),
            comparer,
            registry);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(BuiltInContractProfiles.SampleModelName, BuiltInContractProfiles.SampleProfileId), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        Assert.IsTrue(artifactStore.SavedBodies
            .Where(pair => pair.Key.Contains("/canonical/", StringComparison.OrdinalIgnoreCase))
            .All(pair => pair.Value.Contains("<Envelope", StringComparison.Ordinal)));
        Assert.IsTrue(sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.B).Body.Contains("\"lookupId\":\"123\"", StringComparison.Ordinal));
    }

    private static BasicComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        CapturingEndpointRequestSender sender,
        IContractProfile profile)
    {
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        return new BasicComparisonRunExecutor(
            new FakeRequestBatchStore(manifest, "<request />"),
            sender,
            artifactStore,
            new FakeRunDetailStore(),
            new HashOnlyResponseComparer(),
            CreateRegistry(profile));
    }

    private static ContractProfileRegistry CreateRegistry(IContractProfile profile)
    {
        ContractProfileRegistry registry = new ContractProfileRegistry();
        registry.Register(profile);
        return registry;
    }

    private static RequestBatchManifest CreateManifest(RequestItem request) =>
        new RequestBatchManifest(new RequestBatchReference("batch-1"), new[] { request });

    private static ComparisonRun CreateRun(
        string modelName = "CanonicalModel",
        string profileId = "profile-a") =>
        ComparisonRun.Create(
            new RunId("run-1"),
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://service-a.example.test")),
                new EndpointDefinition(new Uri("https://service-b.example.test")),
                TimeSpan.FromSeconds(30),
                2,
                modelName,
                contractProfileSelection: new ContractProfileSelection(profileId)))
            .Start();

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private class FakeContractProfile : IContractProfile
    {
        public string ProfileId => "profile-a";

        public string ResponseModelName => "CanonicalModel";

        public string? ProfileVersion => "1";

        public Type EndpointARequestType => typeof(object);

        public Type EndpointBRequestType => typeof(object);

        public Type CanonicalResponseType => typeof(object);

        public Type EndpointBResponseType => typeof(object);

        public ContractEndpointProfile EndpointA => new ContractEndpointProfile(PayloadFormat.Xml, "application/xml", PayloadFormat.Xml, supportedSourceRequestFormats: new[] { PayloadFormat.Xml });

        public ContractEndpointProfile EndpointB => new ContractEndpointProfile(PayloadFormat.Json, "application/vnd.alt+json", PayloadFormat.Json);

        public PayloadFormat CanonicalResponseFormat => PayloadFormat.Json;

        public string CanonicalResponseContentType => "application/json";

        public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules => Array.Empty<IgnoreRuleDefinition>();

        public IReadOnlyDictionary<string, string> CanonicalToEndpointResponseMaskPathMap => new Dictionary<string, string>();

        public ValueTask<PreparedContractRequest> PrepareRequestAsync(
            EndpointSlot endpoint,
            ContractRequestPreparationContext context,
            CancellationToken cancellationToken = default)
        {
            if (endpoint == EndpointSlot.A)
            {
                ContractPayload sourcePayload = new ContractPayload(
                    context.SourceFormat,
                    context.SourceContentType,
                    context.OpenSourceRequestBodyAsync,
                    context.Request.ContentLength);
                return ValueTask.FromResult(new PreparedContractRequest(sourcePayload, ProfileId));
            }

            return ValueTask.FromResult(new PreparedContractRequest(
                ContractPayload.FromBytes(Encoding.UTF8.GetBytes("alternate-request"), PayloadFormat.Json, EndpointB.RequestContentType),
                ProfileId,
                new Dictionary<string, string> { ["X-Override"] = "profile", ["SOAPAction"] = "urn:profile" }));
        }

        public ValueTask<NormalizedContractResponse> NormalizeResponseAsync(
            EndpointSlot endpoint,
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            endpoint == EndpointSlot.A
                ? NormalizeEndpointAResponseAsync(context, cancellationToken)
                : NormalizeEndpointBResponseAsync(context, cancellationToken);

        protected virtual ValueTask<NormalizedContractResponse> NormalizeEndpointAResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateNormalizedResponse());

        protected virtual ValueTask<NormalizedContractResponse> NormalizeEndpointBResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateNormalizedResponse());

        protected NormalizedContractResponse CreateNormalizedResponse() =>
            new NormalizedContractResponse(
                ContractPayload.FromBytes(Encoding.UTF8.GetBytes("{\"id\":1}"), PayloadFormat.Json, "application/json"),
                ProfileId);
    }

    private sealed class ThrowingNormalizationProfile : FakeContractProfile
    {
        protected override ValueTask<NormalizedContractResponse> NormalizeEndpointAResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Normalization failed.");
    }

    private sealed class ReadingNormalizationProfile : FakeContractProfile
    {
        public List<int> SourceResponseLengths { get; } = new List<int>();

        protected override async ValueTask<NormalizedContractResponse> NormalizeEndpointAResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            await ReadSourceAndCreateNormalizedResponseAsync(context, cancellationToken).ConfigureAwait(false);

        protected override async ValueTask<NormalizedContractResponse> NormalizeEndpointBResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            await ReadSourceAndCreateNormalizedResponseAsync(context, cancellationToken).ConfigureAwait(false);

        private async ValueTask<NormalizedContractResponse> ReadSourceAndCreateNormalizedResponseAsync(
            ContractResponseNormalizationContext context,
            CancellationToken cancellationToken)
        {
            await using Stream stream = await context.OpenSourceResponseBodyAsync(cancellationToken).ConfigureAwait(false);
            using StreamReader reader = new StreamReader(stream, Encoding.UTF8);
            string source = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            SourceResponseLengths.Add(source.Length);
            return CreateNormalizedResponse();
        }
    }

    private sealed class CapturingEndpointRequestSender : IEndpointRequestSender
    {
        private readonly Func<EndpointRequest, string> responseFactory;

        public CapturingEndpointRequestSender(Func<EndpointRequest, string> responseFactory)
        {
            this.responseFactory = responseFactory;
        }

        public List<CapturedRequest> CapturedRequests { get; } = new List<CapturedRequest>();

        public async Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            CapturedRequests.Add(new CapturedRequest(
                request.Endpoint,
                body,
                request.ContentType,
                new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase)));

            string response = responseFactory(request);
            return new EndpointResponse(200, request.Endpoint == EndpointSlot.A ? "application/xml" : "application/json", new MemoryStream(Encoding.UTF8.GetBytes(response)));
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

        public Task<Stream> OpenReadAsync(
            ArtifactReference artifact,
            CancellationToken cancellationToken = default)
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


