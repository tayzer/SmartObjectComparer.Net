using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class BasicComparisonRunExecutorTests
{
    [TestMethod]
    public async Task ExecuteAsync_WhenResponsesMatch_CompletesWithEqualSummary()
    {
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            FakeEndpointRequestSender.ForBody("same"));

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.TotalPairs);
        Assert.AreEqual(1, summary.EqualPairs);
        Assert.AreEqual(0, summary.DifferentPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenResponsesDiffer_CompletesWithDifferentSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
            request.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream("a"))
                : new EndpointResponse(200, "application/json", CreateStream("b")));
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.DifferentPairs);
        Assert.AreEqual(0, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenStatusCodesMismatch_CompletesWithStatusMismatchSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
            request.Endpoint == EndpointSlot.A
                ? new EndpointResponse(200, "application/json", CreateStream("same"))
                : new EndpointResponse(500, "application/json", CreateStream("same")));
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.StatusCodeMismatchPairs);
        Assert.AreEqual(0, summary.ErrorPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenEndpointSenderThrows_CompletesWithErrorSummary()
    {
        FakeEndpointRequestSender sender = new FakeEndpointRequestSender(request =>
        {
            if (request.Endpoint == EndpointSlot.B)
            {
                throw new InvalidOperationException("Endpoint B failed.");
            }

            return new EndpointResponse(200, "application/json", CreateStream("same"));
        });
        BasicComparisonRunExecutor executor = CreateExecutor(
            CreateBatch(new[] { new RequestItem("one.json", "application/json", 2) }),
            sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(), new CapturingProgressReporter());

        Assert.AreEqual(1, summary.ErrorPairs);
        Assert.AreEqual(0, summary.EqualPairs);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenMultipleRequestsExist_ReportsProgressAndHonorsConcurrencyLimit()
    {
        RequestItem[] requests = Enumerable
            .Range(1, 5)
            .Select(index => new RequestItem($"request-{index}.json", "application/json", 2))
            .ToArray();
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same", TimeSpan.FromMilliseconds(25));
        CapturingProgressReporter progressReporter = new CapturingProgressReporter();
        BasicComparisonRunExecutor executor = CreateExecutor(CreateBatch(requests), sender);

        RunResultSummary summary = await executor.ExecuteAsync(CreateRun(maxConcurrency: 2), progressReporter);

        Assert.AreEqual(5, summary.TotalPairs);
        Assert.IsTrue(sender.MaxActiveRequestPaths <= 2);
        Assert.IsTrue(progressReporter.Events.Any(progress =>
            progress.Status == RunStatus.Executing
            && progress.Progress.CompletedItems == 5
            && progress.Progress.TotalItems == 5));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenRunUsesHeaders_MergesEndpointAndRequestHeaders()
    {
        RequestItem request = new RequestItem(
            "one.json",
            "application/json",
            2,
            new Dictionary<string, string> { ["X-Common"] = "request", ["X-Override"] = "request" },
            new Dictionary<string, string> { ["X-A"] = "request-a" });
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same");
        BasicComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender);
        ComparisonRun run = CreateRun(
            endpointAHeaders: new Dictionary<string, string> { ["X-Endpoint"] = "a", ["X-Override"] = "endpoint" });

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        EndpointRequest endpointARequest = sender.SentRequests.Single(sentRequest => sentRequest.Endpoint == EndpointSlot.A);
        Assert.AreEqual("a", endpointARequest.Headers["X-Endpoint"]);
        Assert.AreEqual("request", endpointARequest.Headers["X-Common"]);
        Assert.AreEqual("request", endpointARequest.Headers["X-Override"]);
        Assert.AreEqual("request-a", endpointARequest.Headers["X-A"]);
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenContentTypeOverrideIsConfigured_SendsOverride()
    {
        RequestItem request = new RequestItem("one.txt", "text/plain", 2);
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("same");
        BasicComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender);
        ComparisonRun run = CreateRun(
            requestExecutionOptions: new RequestExecutionOptions("application/json"));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        Assert.IsTrue(sender.SentRequests.All(sentRequest => sentRequest.ContentType == "application/json"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WhenMaskRulesExist_PersistsMaskedArtifacts()
    {
        RequestItem request = new RequestItem("one.json", "application/json", 2);
        FakeEndpointRequestSender sender = FakeEndpointRequestSender.ForBody("{\"token\":\"secret-1234\"}");
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        BasicComparisonRunExecutor executor = CreateExecutor(CreateBatch(new[] { request }), sender, artifactStore);
        ComparisonRun run = CreateRun(
            comparisonOptions: new ComparisonOptions(
                maskRules: new[] { new MaskRuleDefinition("token", preserveLastCharacters: 4) }));

        await executor.ExecuteAsync(run, new CapturingProgressReporter());

        Assert.AreEqual(2, artifactStore.SavedBodies.Count);
        Assert.IsTrue(artifactStore.SavedBodies.Values.All(body => body.Contains("*******1234", StringComparison.Ordinal)));
        Assert.IsTrue(artifactStore.SavedBodies.Values.All(body => !body.Contains("secret-1234", StringComparison.Ordinal)));
    }

    private static BasicComparisonRunExecutor CreateExecutor(
        RequestBatchManifest manifest,
        FakeEndpointRequestSender sender,
        FakeRunArtifactStore? artifactStore = null,
        IResponseComparer? responseComparer = null)
    {
        FakeRequestBatchStore requestBatchStore = new FakeRequestBatchStore(manifest);
        return responseComparer is null
            ? new BasicComparisonRunExecutor(
                requestBatchStore,
                sender,
                artifactStore ?? new FakeRunArtifactStore(),
                new FakeRunDetailStore())
            : new BasicComparisonRunExecutor(
                requestBatchStore,
                sender,
                artifactStore ?? new FakeRunArtifactStore(),
                new FakeRunDetailStore(),
                responseComparer);
    }

    private static RequestBatchManifest CreateBatch(IReadOnlyList<RequestItem> requests) =>
        new RequestBatchManifest(new RequestBatchReference("batch-1"), requests);

    private static ComparisonRun CreateRun(
        int maxConcurrency = 4,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null) =>
        ComparisonRun
            .Create(
                new RunId("run-1"),
                new RunOptions(
                    new RequestBatchReference("batch-1"),
                    new EndpointDefinition(new Uri("https://service-a.example.test"), headers: endpointAHeaders),
                    new EndpointDefinition(new Uri("https://service-b.example.test")),
                    TimeSpan.FromSeconds(30),
                    maxConcurrency,
                    comparisonOptions: comparisonOptions,
                    requestExecutionOptions: requestExecutionOptions))
            .Start();

    private static MemoryStream CreateStream(string value) =>
        new MemoryStream(Encoding.UTF8.GetBytes(value));

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        private readonly RequestBatchManifest manifest;

        public FakeRequestBatchStore(RequestBatchManifest manifest)
        {
            this.manifest = manifest;
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
            Task.FromResult<Stream>(CreateStream($"request:{request.RelativePath}"));
    }

    private sealed class FakeEndpointRequestSender : IEndpointRequestSender
    {
        private readonly Func<EndpointRequest, EndpointResponse> send;
        private readonly TimeSpan delay;
        private readonly object gate = new object();
        private readonly Dictionary<string, int> activeRequestPathCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        public FakeEndpointRequestSender(
            Func<EndpointRequest, EndpointResponse> send,
            TimeSpan? delay = null)
        {
            this.send = send;
            this.delay = delay ?? TimeSpan.Zero;
        }

        public List<EndpointRequest> SentRequests { get; } = new List<EndpointRequest>();

        public int MaxActiveRequestPaths { get; private set; }

        public static FakeEndpointRequestSender ForBody(string body, TimeSpan? delay = null) =>
            new FakeEndpointRequestSender(
                _ => new EndpointResponse(200, "application/json", CreateStream(body)),
                delay);

        public async Task<EndpointResponse> SendAsync(
            EndpointRequest request,
            CancellationToken cancellationToken = default)
        {
            EnterRequestPath(request.Request.RelativePath);
            try
            {
                lock (gate)
                {
                    SentRequests.Add(request);
                }

                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }

                return send(request);
            }
            finally
            {
                ExitRequestPath(request.Request.RelativePath);
            }
        }

        private void EnterRequestPath(string relativePath)
        {
            lock (gate)
            {
                activeRequestPathCounts.TryGetValue(relativePath, out int count);
                activeRequestPathCounts[relativePath] = count + 1;
                MaxActiveRequestPaths = Math.Max(MaxActiveRequestPaths, activeRequestPathCounts.Count);
            }
        }

        private void ExitRequestPath(string relativePath)
        {
            lock (gate)
            {
                int count = activeRequestPathCounts[relativePath] - 1;
                if (count == 0)
                {
                    activeRequestPathCounts.Remove(relativePath);
                    return;
                }

                activeRequestPathCounts[relativePath] = count;
            }
        }
    }

    private sealed class FakeRunArtifactStore : IRunArtifactStore
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
        public IReadOnlyList<RequestPairResult> SavedResults { get; private set; } = Array.Empty<RequestPairResult>();

        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default)
        {
            SavedResults = results;
            return Task.FromResult(new RunDetailReference($"runs/{runId.Value}/details/index.json"));
        }

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedResults);
    }

    private sealed class CapturingProgressReporter : IRunProgressReporter
    {
        public List<(RunStatus Status, RunProgress Progress)> Events { get; } = new List<(RunStatus Status, RunProgress Progress)>();

        public Task ReportAsync(
            RunStatus status,
            RunProgress progress,
            CancellationToken cancellationToken = default)
        {
            Events.Add((status, progress));
            return Task.CompletedTask;
        }
    }
}
