using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class CompareNetObjectsResponseComparerTests
{
    [TestMethod]
    public async Task CompareAsync_WhenObjectsAreEqualButRawHashesDiffer_ReturnsEqual()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Alpha" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
        Assert.AreEqual(0, result.DifferenceCount);
    }

    [TestMethod]
    public async Task CompareAsync_WhenObjectsDiffer_ReturnsDifferentWithMetadata()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Beta" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Different, result.Outcome);
        Assert.IsTrue(result.DifferenceCount > 0);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath.Contains("Name", StringComparison.Ordinal)));
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoreCompleteRuleMatchesDifference_ReturnsEqual()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "Beta" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                ignoreRules: new[] { new IgnoreRuleDefinition("Name") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringStringCase_SuppressesCasingOnlyDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "alpha" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreStringCase: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringTrailingWhitespace_SuppressesEndWhitespaceDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Description = "Hello" }),
            ("b", () => new SampleResponse { Id = 1, Description = "Hello   " }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreTrailingWhitespaceAtEnd: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenTreatingNullAndEmptyCollectionsAsEqual_SuppressesCollectionDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Values = null }),
            ("b", () => new SampleResponse { Id = 1, Values = new List<int>() }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(treatNullAndEmptyCollectionsAsEqual: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        string differences = string.Join(" | ", result.Differences.Select(difference => $"{difference.PropertyPath}:{difference.ValueA}->{difference.ValueB}"));
        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome, differences);
    }

    [TestMethod]
    public async Task CompareAsync_WhenIgnoringCollectionOrder_SuppressesReorderedCollectionDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Values = new List<int> { 1, 2, 3 } }),
            ("b", () => new SampleResponse { Id = 1, Values = new List<int> { 3, 2, 1 } }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreCollectionOrder: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenSmartIgnoreMatchesPropertyName_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, CorrelationId = "a" }),
            ("b", () => new SampleResponse { Id = 1, CorrelationId = "b" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "CorrelationId") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenSmartIgnoreMatchesNamePattern_SuppressesDifference()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, CorrelationId = "a" }),
            ("b", () => new SampleResponse { Id = 1, CorrelationId = "b" }));

        RequestPairResult result = await comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(
                smartIgnoreRules: new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.NamePattern, ".*CorrelationId$") })),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        Assert.AreEqual(RequestPairOutcome.Equal, result.Outcome);
    }

    [TestMethod]
    public async Task CompareAsync_WhenConcurrentRunsUseDifferentOptions_ProducesIsolatedResults()
    {
        CompareNetObjectsResponseComparer comparer = CreateComparer(
            ("a", () => new SampleResponse { Id = 1, Name = "Alpha" }),
            ("b", () => new SampleResponse { Id = 1, Name = "alpha" }));

        Task<RequestPairResult> ignoredCaseTask = comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(comparisonOptions: new ComparisonOptions(ignoreStringCase: true)),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);
        Task<RequestPairResult> caseSensitiveTask = comparer.CompareAsync(
            CreateRequest(),
            CreateOptions(),
            CreateResponse(EndpointSlot.A, "a"),
            CreateResponse(EndpointSlot.B, "b"),
            null);

        RequestPairResult[] results = await Task.WhenAll(ignoredCaseTask, caseSensitiveTask);

        Assert.AreEqual(RequestPairOutcome.Equal, results[0].Outcome);
        Assert.AreEqual(RequestPairOutcome.Different, results[1].Outcome);
    }

    private static CompareNetObjectsResponseComparer CreateComparer(
        params (string ArtifactId, Func<SampleResponse> Factory)[] models)
    {
        InMemoryArtifactStore artifactStore = new InMemoryArtifactStore(models.Select(model => model.ArtifactId));
        FakeResponseBodyDeserializer deserializer = new FakeResponseBodyDeserializer(models);
        return new CompareNetObjectsResponseComparer(artifactStore, deserializer);
    }

    private static RequestItem CreateRequest() => new RequestItem("one.json", "application/json", 2);

    private static RunOptions CreateOptions(ComparisonOptions? comparisonOptions = null) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            "Sample",
            comparisonOptions);

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId)
    {
        byte[] content = Encoding.UTF8.GetBytes(artifactId);

        return new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference(artifactId, "application/json"),
            200,
            "application/json",
            content.Length,
            Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant());
    }

    private sealed class InMemoryArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> contentByArtifactId;

        public InMemoryArtifactStore(IEnumerable<string> artifactIds)
        {
            contentByArtifactId = artifactIds.ToDictionary(
                artifactId => artifactId,
                artifactId => Encoding.UTF8.GetBytes(artifactId),
                StringComparer.Ordinal);
        }

        public Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(
            ArtifactReference artifact,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(contentByArtifactId[artifact.ArtifactId]));
    }

    private sealed class FakeResponseBodyDeserializer : IResponseBodyDeserializer
    {
        private readonly Dictionary<string, Func<SampleResponse>> models;

        public FakeResponseBodyDeserializer(IEnumerable<(string ArtifactId, Func<SampleResponse> Factory)> models)
        {
            this.models = models.ToDictionary(model => model.ArtifactId, model => model.Factory, StringComparer.Ordinal);
        }

        public async Task<object> DeserializeAsync(
            string modelName,
            Stream body,
            string? contentType,
            ComparisonOptions comparisonOptions,
            CancellationToken cancellationToken = default)
        {
            using StreamReader reader = new StreamReader(body, Encoding.UTF8);
            string artifactId = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
            return models[artifactId]();
        }
    }

    public sealed class SampleResponse
    {
        public int Id { get; init; }

        public string? Name { get; init; }

        public string? Description { get; init; }

        public string? CorrelationId { get; init; }

        public List<int>? Values { get; init; }
    }
}

