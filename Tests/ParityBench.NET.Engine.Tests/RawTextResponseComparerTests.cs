using System.Security.Cryptography;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class RawTextResponseComparerTests
{
    [TestMethod]
    public async Task CompareAsync_WhenStatusCodesMismatch_AddsHttpStatusDifferenceAndRawTextDifferences()
    {
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        ResponseArtifactMetadata responseA = artifactStore.Add(EndpointSlot.A, 200, "ok\nsame");
        ResponseArtifactMetadata responseB = artifactStore.Add(EndpointSlot.B, 500, "error\nsame");
        RawTextResponseComparer comparer = CreateComparer(artifactStore);

        RequestPairResult result = await comparer.CompareAsync(CreateRequest(), CreateOptions(), responseA, responseB, null);

        Assert.AreEqual(RequestPairOutcome.StatusCodeMismatch, result.Outcome);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath == "HttpStatus"));
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath == "Body.Line[1]"));
        Assert.IsNull(result.AreEqual);
    }

    [TestMethod]
    public async Task CompareAsync_WhenBothNonSuccessBodiesDiffer_ReturnsBothNonSuccessWithRawTextDifferences()
    {
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        ResponseArtifactMetadata responseA = artifactStore.Add(EndpointSlot.A, 500, "first");
        ResponseArtifactMetadata responseB = artifactStore.Add(EndpointSlot.B, 503, "second");
        RawTextResponseComparer comparer = CreateComparer(artifactStore);

        RequestPairResult result = await comparer.CompareAsync(CreateRequest(), CreateOptions(), responseA, responseB, null);

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, result.Outcome);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath == "Body.Line[1]"));
        Assert.IsNull(result.AreEqual);
    }

    [TestMethod]
    public async Task CompareAsync_WhenBothNonSuccessBodiesMatch_ReturnsBothNonSuccessWithoutEqualSummary()
    {
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        ResponseArtifactMetadata responseA = artifactStore.Add(EndpointSlot.A, 500, "same");
        ResponseArtifactMetadata responseB = artifactStore.Add(EndpointSlot.B, 500, "same");
        RawTextResponseComparer comparer = CreateComparer(artifactStore);

        RequestPairResult result = await comparer.CompareAsync(CreateRequest(), CreateOptions(), responseA, responseB, null);

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, result.Outcome);
        Assert.AreEqual(0, result.DifferenceCount);
        Assert.IsNull(result.AreEqual);
        Assert.IsTrue(result.OutcomeMessage?.Contains("non-success", StringComparison.OrdinalIgnoreCase) == true);
    }

    [TestMethod]
    public async Task CompareAsync_WhenNonSuccessBodyIsLarge_TruncatesPreview()
    {
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        string largeBody = new string('a', 6000);
        ResponseArtifactMetadata responseA = artifactStore.Add(EndpointSlot.A, 500, largeBody);
        ResponseArtifactMetadata responseB = artifactStore.Add(EndpointSlot.B, 500, largeBody);
        RawTextResponseComparer comparer = CreateComparer(artifactStore);

        RequestPairResult result = await comparer.CompareAsync(CreateRequest(), CreateOptions(), responseA, responseB, null);

        Assert.AreEqual(RequestPairOutcome.BothNonSuccess, result.Outcome);
        Assert.IsTrue(result.Differences.Any(difference => difference.PropertyPath == "BodyPreview"));
        Assert.IsTrue(result.Differences.Single(difference => difference.PropertyPath == "BodyPreview").Message?.Contains("5120", StringComparison.Ordinal) == true);
    }

    private static RawTextResponseComparer CreateComparer(FakeRunArtifactStore artifactStore) =>
        new RawTextResponseComparer(artifactStore, new HashOnlyResponseComparer());

    private static RequestItem CreateRequest() =>
        new RequestItem("one.txt", "text/plain", 4);

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            comparisonOptions: new ComparisonOptions(maxDifferences: 20));

    private static string ToSha256(byte[] content) =>
        Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();

    private sealed class FakeRunArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> contentByArtifact = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

        public ResponseArtifactMetadata Add(EndpointSlot endpoint, int statusCode, string content)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(content);
            string artifactId = $"artifact-{endpoint}-{contentByArtifact.Count}";
            contentByArtifact[artifactId] = bytes;

            return new ResponseArtifactMetadata(
                endpoint,
                new ArtifactReference(artifactId, "text/plain"),
                statusCode,
                "text/plain",
                bytes.Length,
                ToSha256(bytes));
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
            Task.FromResult<Stream>(new MemoryStream(contentByArtifact[artifact.ArtifactId]));
    }
}
