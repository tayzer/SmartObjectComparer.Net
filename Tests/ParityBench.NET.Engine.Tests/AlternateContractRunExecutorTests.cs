using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class AlternateContractRunExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenAlternateProfileIsConfigured_TransformsEndpointBRequestAndSuppressesSoapAction()
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
            new FakeAlternateContractProfile());

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        CapturedRequest endpointARequest = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.A);
        CapturedRequest endpointBRequest = sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.B);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual("urn:common", endpointARequest.Headers["SOAPAction"]);
        Assert.IsFalse(endpointBRequest.Headers.ContainsKey("SOAPAction"));
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
            new FakeAlternateContractProfile());

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
    public async Task ExecuteAsync_WhenUsingBuiltInSampleProfile_ComparesCanonicalArtifacts()
    {
        RequestItem request = new RequestItem("one.xml", "application/xml", 10);
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        IContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        IAlternateContractProfile sampleProfile = BuiltInAlternateContractProfiles.CreateSampleSoapToJson(serializer);
        AlternateContractProfileRegistry registry = CreateRegistry(sampleProfile);
        ResponseModelRegistry modelRegistry = new ResponseModelRegistry();
        modelRegistry.Register<SampleSoapCustomerLookupResponseEnvelope>(BuiltInAlternateContractProfiles.SampleModelName);
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

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(BuiltInAlternateContractProfiles.SampleModelName, BuiltInAlternateContractProfiles.SampleProfileId), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.EqualPairs);
        Assert.IsTrue(artifactStore.SavedBodies.Values.All(body => body.Contains("<Envelope", StringComparison.Ordinal)));
        Assert.IsTrue(sender.CapturedRequests.Single(sent => sent.Endpoint == EndpointSlot.B).Body.Contains("\"lookupId\":\"123\"", StringComparison.Ordinal));
    }

    private static BasicComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        CapturingEndpointRequestSender sender,
        IAlternateContractProfile profile)
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

    private static AlternateContractProfileRegistry CreateRegistry(IAlternateContractProfile profile)
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
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
                alternateContractOptions: new AlternateContractOptions(profileId)))
            .Start();

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private class FakeAlternateContractProfile : IAlternateContractProfile
    {
        public string ProfileId => "profile-a";

        public string CanonicalModelName => "CanonicalModel";

        public Type CanonicalRequestType => typeof(object);

        public Type AlternateRequestType => typeof(object);

        public Type CanonicalResponseType => typeof(object);

        public Type AlternateResponseType => typeof(object);

        public IReadOnlyCollection<PayloadFormat> SupportedSourceRequestFormats => new[] { PayloadFormat.Xml };

        public PayloadFormat AlternateRequestFormat => PayloadFormat.Json;

        public string AlternateRequestContentType => "application/vnd.alt+json";

        public PayloadFormat AlternateResponseFormat => PayloadFormat.Json;

        public PayloadFormat CanonicalResponseFormat => PayloadFormat.Json;

        public string CanonicalResponseContentType => "application/json";

        public string? SuggestedEndpointAId => null;

        public string? SuggestedEndpointBId => null;

        public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules => Array.Empty<IgnoreRuleDefinition>();

        public IReadOnlyDictionary<string, string> CanonicalToAlternateResponseMaskPathMap => new Dictionary<string, string>();

        public ValueTask<PreparedAlternateContractRequest> PrepareEndpointBRequestAsync(
            AlternateContractRequestPreparationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new PreparedAlternateContractRequest(
                Encoding.UTF8.GetBytes("alternate-request"),
                AlternateRequestContentType,
                PayloadFormat.Json,
                ProfileId,
                new Dictionary<string, string> { ["X-Override"] = "profile", ["SOAPAction"] = "urn:profile" }));

        public virtual ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointAResponseAsync(
            AlternateContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateNormalizedResponse());

        public virtual ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointBResponseAsync(
            AlternateContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(CreateNormalizedResponse());

        private NormalizedAlternateContractResponse CreateNormalizedResponse() =>
            new NormalizedAlternateContractResponse(
                Encoding.UTF8.GetBytes("{\"id\":1}"),
                PayloadFormat.Json,
                "application/json",
                ProfileId);
    }

    private sealed class ThrowingNormalizationProfile : FakeAlternateContractProfile
    {
        public override ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointAResponseAsync(
            AlternateContractResponseNormalizationContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Normalization failed.");
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
        private readonly Dictionary<string, byte[]> savedContent = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> SavedBodies => savedContent
            .ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetString(pair.Value), StringComparer.OrdinalIgnoreCase);

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
            savedContent[artifactId] = content;

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
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(savedContent[artifact.ArtifactId]));
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


