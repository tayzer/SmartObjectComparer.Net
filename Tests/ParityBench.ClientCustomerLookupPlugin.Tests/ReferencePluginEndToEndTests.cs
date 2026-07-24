using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Plugins;

namespace ParityBench.ClientCustomerLookupPlugin.Tests;

/// <summary>
/// Proves the whole extensibility path with the real reference plugin package:
/// discovered and loaded from disk into an isolated context, its token-exchange and
/// mapping middleware spliced into the built-in pipeline, driven entirely by a run
/// profile's plugin selection — no host references the plugin at build time.
/// </summary>
[TestClass]
public sealed class ReferencePluginEndToEndTests
{
    private const string EndpointBSubscriptionKey = "mock-endpoint-b-subscription-key";
    private const string PrimaryTokenSubscriptionKey = "mock-primary-token-subscription-key";
    private const string FinalTokenSubscriptionKey = "mock-final-token-subscription-key";
    private const string PrimaryToken = "mock-primary-token";
    private const string FinalToken = "mock-final-token";

    // The plugin is loaded from disk, never referenced, so its stable ids are
    // written here as the literals a real run profile would carry.
    private const string PluginId = "client.customer-lookup";
    private const string ComparisonId = "client.customer-lookup.soap-vs-json";
    private const string RequestStepId = "client.customer-lookup.request";
    private const string SubscriptionKeyHeader = "Ocp-Apim-Subscription-Key";

    private static readonly string PluginPackageDirectory = ResolvePluginPackageDirectory();

    [TestMethod]
    public async Task Run_UsingReferencePlugin_ExchangesToken_MapsRequest_AndComparesCanonicalResponses()
    {
        using TempPluginRoot pluginRoot = TempPluginRoot.InstallReferencePackage();
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        CapturingEndpointRequestSender sender = new CapturingEndpointRequestSender();
        TokenEndpointHandler tokenHandler = new TokenEndpointHandler();

        ComparisonRunExecutor executor = BuildExecutor(pluginRoot, artifactStore, sender, tokenHandler);
        ComparisonRun run = CreateRun();

        RunResultSummary summary = await executor.ExecuteAsync(run, new NoOpProgressReporter());

        // One pair was produced: both endpoints were projected onto the canonical
        // type and diffed, which only happens if the plugin's mapping ran on both.
        Assert.AreEqual(1, summary.TotalPairs);
        Assert.AreEqual(0, summary.ErrorPairs, "The plugin pipeline should not have errored.");

        // The token exchange happened, primary before final.
        CollectionAssert.AreEqual(
            new[] { "/client/token/primary", "/client/token/final" },
            tokenHandler.CalledPaths.ToArray());

        // Endpoint B received the mapped JSON request with the exchanged bearer token
        // and the subscription key the plugin attached.
        CapturedRequest endpointB = sender.Captured.Single(request => request.Endpoint == EndpointSlot.B);
        Assert.AreEqual("application/json", endpointB.ContentType);
        StringAssert.Contains(endpointB.Body, "\"customerId\":\"C-1\"");
        Assert.AreEqual($"Bearer {FinalToken}", endpointB.Headers["Authorization"]);
        Assert.AreEqual(EndpointBSubscriptionKey, endpointB.Headers[SubscriptionKeyHeader]);

        // Endpoint A kept the source SOAP and gained its SOAPAction header.
        CapturedRequest endpointA = sender.Captured.Single(request => request.Endpoint == EndpointSlot.A);
        Assert.AreEqual("urn:ClientCustomerLookup", endpointA.Headers["SOAPAction"]);

        // Both sides persisted a canonical projection, which is what got compared.
        Assert.AreEqual(2, artifactStore.SavedBodies.Keys.Count(key => key.Contains("/canonical/", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public async Task Run_WhenTokenSubscriptionKeyIsWrong_FailsThePairWithoutErroringTheRun()
    {
        using TempPluginRoot pluginRoot = TempPluginRoot.InstallReferencePackage();
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore();
        TokenEndpointHandler tokenHandler = new TokenEndpointHandler();

        ComparisonRunExecutor executor = BuildExecutor(pluginRoot, artifactStore, new CapturingEndpointRequestSender(), tokenHandler);
        // A profile whose secret resolves to the wrong value: the token endpoint rejects it.
        ComparisonRun run = CreateRun(primaryTokenSubscriptionKey: "wrong-key");

        RunResultSummary summary = await executor.ExecuteAsync(run, new NoOpProgressReporter());

        Assert.AreEqual(1, summary.TotalPairs);
        Assert.AreEqual(1, summary.ErrorPairs);
    }

    private static ComparisonRunExecutor BuildExecutor(
        TempPluginRoot pluginRoot,
        InMemoryArtifactStore artifactStore,
        CapturingEndpointRequestSender sender,
        TokenEndpointHandler tokenHandler)
    {
        PluginCatalog catalog = new PluginCatalog(new[] { pluginRoot.Path });
        Assert.AreEqual(1, catalog.Packages.Count, "The reference package should be discovered from its manifest.");

        JsonXmlContractPayloadSerializer serializer = new JsonXmlContractPayloadSerializer();
        HttpClient tokenClient = new HttpClient(tokenHandler);

        PluginComparisonPlanFactory planFactory = new PluginComparisonPlanFactory(
            catalog,
            new PluginLoader(),
            hostServices =>
            {
                hostServices.AddSingleton<IContractPayloadSerializer>(serializer);
                hostServices.AddSingleton(tokenClient);
            });

        return new ComparisonRunExecutor(
            new FakeRequestBatchStore(SoapRequestBody),
            sender,
            artifactStore,
            new FakeRunDetailStore(),
            new HashOnlyResponseComparer(),
            planFactory,
            contractPayloadSerializer: serializer);
    }

    private static ComparisonRun CreateRun(string primaryTokenSubscriptionKey = PrimaryTokenSubscriptionKey)
    {
        Dictionary<string, IReadOnlyDictionary<string, string>> stepConfiguration =
            new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [RequestStepId] = new Dictionary<string, string>
                {
                    // Secret values arrive already resolved from the secret store.
                    ["primaryTokenUrl"] = "http://endpoint.test/client/token/primary",
                    ["primaryTokenSubscriptionKey"] = primaryTokenSubscriptionKey,
                    ["finalTokenUrl"] = "http://endpoint.test/client/token/final",
                    ["finalTokenSubscriptionKey"] = FinalTokenSubscriptionKey,
                    ["endpointBSubscriptionKey"] = EndpointBSubscriptionKey,
                },
            };

        RunOptions options = new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("http://endpoint.test/client/customer-lookup/soap")),
            new EndpointDefinition(new Uri("http://endpoint.test/client/customer-lookup/json")),
            TimeSpan.FromSeconds(30),
            2,
            pluginComparison: new PluginComparisonSelection(
                PluginId,
                ComparisonId,
                stepConfiguration: stepConfiguration));

        return ComparisonRun.Create(new RunId("run-1"), options).Start();
    }

    private const string SoapRequestBody =
        "<Envelope><Body><LookupRequest>" +
        "<UserName>svc-user</UserName><Password>svc-pass</Password>" +
        "<CustomerId>C-1</CustomerId><CorrelationId>corr-1</CorrelationId>" +
        "</LookupRequest></Body></Envelope>";

    private const string SoapResponseBody =
        "<Envelope><Body><LookupResponse>" +
        "<StatusCode>OK</StatusCode><CustomerName>Riley Morgan</CustomerName><TraceId>corr-1</TraceId>" +
        "</LookupResponse></Body></Envelope>";

    private static string ResolvePluginPackageDirectory()
    {
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Source", "ParityBench.ClientCustomerLookupPlugin", "bin", configuration, "net10.0");
    }

    private static string ToSha256(byte[] content) => Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class TokenEndpointHandler : HttpMessageHandler
    {
        public List<string> CalledPaths { get; } = new List<string>();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string path = request.RequestUri!.AbsolutePath;
            lock (CalledPaths)
            {
                CalledPaths.Add(path);
            }

            bool authorized = path.EndsWith("/primary", StringComparison.Ordinal)
                ? HasHeader(request, SubscriptionKeyHeader, PrimaryTokenSubscriptionKey)
                : HasHeader(request, SubscriptionKeyHeader, FinalTokenSubscriptionKey);

            if (!authorized)
            {
                return new HttpResponseMessage(HttpStatusCode.Unauthorized);
            }

            string token = path.EndsWith("/primary", StringComparison.Ordinal) ? PrimaryToken : FinalToken;
            await Task.Yield();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new Dictionary<string, string> { ["access_token"] = token }),
            };
        }

        private static bool HasHeader(HttpRequestMessage request, string name, string expected) =>
            request.Headers.TryGetValues(name, out IEnumerable<string>? values)
            && values.Any(value => string.Equals(value, expected, StringComparison.Ordinal));
    }

    private sealed record CapturedRequest(
        EndpointSlot Endpoint,
        string Body,
        string ContentType,
        IReadOnlyDictionary<string, string> Headers);

    private sealed class CapturingEndpointRequestSender : IEndpointRequestSender
    {
        public List<CapturedRequest> Captured { get; } = new List<CapturedRequest>();

        public async Task<EndpointResponse> SendAsync(EndpointRequest request, CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new StreamReader(request.Body, Encoding.UTF8, leaveOpen: true);
            string body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            lock (Captured)
            {
                Captured.Add(new CapturedRequest(
                    request.Endpoint,
                    body,
                    request.ContentType,
                    new Dictionary<string, string>(request.Headers, StringComparer.OrdinalIgnoreCase)));
            }

            return request.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "text/xml", new MemoryStream(Encoding.UTF8.GetBytes(SoapResponseBody)))
                : new EndpointResponse(200, "application/json", new MemoryStream(Encoding.UTF8.GetBytes(EndpointBJsonResponse)));
        }

        private const string EndpointBJsonResponse =
            "{\"details\":{\"resultCode\":\"OK\",\"traceId\":\"corr-1\",\"decisionEngine\":\"EndpointB\"}," +
            "\"apps\":[{\"applicantId\":\"corr-1\",\"profile\":{\"fullName\":\"Riley Morgan\",\"addresses\":[]}," +
            "\"ruleEvaluations\":[],\"flags\":[]}]}";
    }

    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        private readonly string body;
        private readonly RequestBatchManifest manifest;

        public FakeRequestBatchStore(string body)
        {
            this.body = body;
            manifest = new RequestBatchManifest(
                new RequestBatchReference("batch-1"),
                new[] { new RequestItem("one.xml", "text/xml", body.Length) });
        }

        public Task<RequestBatchManifest> StageDirectoryAsync(string sourceDirectory, RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> StageFilesAsync(string sourceDirectory, IReadOnlyList<string> sourceFiles, RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<RequestBatchManifest> LoadManifestAsync(RequestBatchReference batchReference, CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<Stream> OpenRequestBodyAsync(RequestBatchReference batchReference, RequestItem request, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(body)));
    }

    private sealed class InMemoryArtifactStore : IRunArtifactStore
    {
        private readonly object gate = new object();
        private readonly Dictionary<string, byte[]> saved = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyDictionary<string, string> SavedBodies
        {
            get
            {
                lock (gate)
                {
                    return saved.ToDictionary(pair => pair.Key, pair => Encoding.UTF8.GetString(pair.Value), StringComparer.OrdinalIgnoreCase);
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
            using MemoryStream buffer = new MemoryStream();
            await body.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
            byte[] content = buffer.ToArray();
            string artifactId = $"runs/{runId.Value}/artifacts/{endpoint}/{request.RelativePath}";
            lock (gate)
            {
                saved[artifactId] = content;
            }

            return new ResponseArtifactMetadata(
                endpoint,
                new ParityBench.NET.Domain.Runs.ArtifactReference(artifactId, contentType),
                statusCode,
                contentType,
                content.Length,
                ToSha256(content));
        }

        public Task<Stream> OpenReadAsync(ParityBench.NET.Domain.Runs.ArtifactReference artifact, CancellationToken cancellationToken = default)
        {
            lock (gate)
            {
                return Task.FromResult<Stream>(new MemoryStream(saved[artifact.ArtifactId]));
            }
        }
    }

    private sealed class FakeRunDetailStore : IRunDetailStore
    {
        public Task<RunDetailReference> SaveDetailsAsync(RunId runId, IReadOnlyList<RequestPairResult> results, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailReference($"runs/{runId.Value}/details/index.json"));

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(RunDetailReference detailReference, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RequestPairResult>>(Array.Empty<RequestPairResult>());

        public Task<RunDetailPage> LoadPageAsync(RunDetailReference detailReference, RunDetailQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailPage(Array.Empty<RequestPairResult>(), 0, query.Offset, query.Limit));
    }

    private sealed class NoOpProgressReporter : IRunProgressReporter
    {
        public Task ReportAsync(RunStatus status, RunProgress progress, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class TempPluginRoot : IDisposable
    {
        private TempPluginRoot(string path) => Path = path;

        public string Path { get; }

        public static TempPluginRoot InstallReferencePackage()
        {
            string root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paritybench-ref-plugin", Guid.NewGuid().ToString("n"));
            string packageDirectory = System.IO.Path.Combine(root, "client.customer-lookup");
            Directory.CreateDirectory(packageDirectory);

            foreach (string file in Directory.EnumerateFiles(PluginPackageDirectory))
            {
                File.Copy(file, System.IO.Path.Combine(packageDirectory, System.IO.Path.GetFileName(file)), overwrite: true);
            }

            return new TempPluginRoot(root);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // The plugin ALC keeps its assemblies loaded, so a leftover temp
                // directory is not worth failing a test over.
            }
        }
    }
}
