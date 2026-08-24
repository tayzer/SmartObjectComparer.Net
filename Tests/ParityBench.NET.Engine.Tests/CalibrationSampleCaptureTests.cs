using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class CalibrationSampleCaptureTests
{
    [TestMethod]
    public async Task CaptureAsync_CopiesPrivateRawPairBeforeRetentionAndWritesOwnershipMarker()
    {
        string root = Path.Combine(Path.GetTempPath(), "ParityBenchCalibrationCapture", Guid.NewGuid().ToString("N"));
        try
        {
            RunId runId = new("capture-run");
            RequestPairResult result = CreateResult(runId, "nested/request.xml");
            MemoryArtifactStore artifacts = new();
            artifacts.Add($"runs/{runId.Value}/artifacts/A/nested/request.xml", "response-a");
            artifacts.Add($"runs/{runId.Value}/artifacts/B/nested/request.xml", "response-b");
            ComparisonRun run = ComparisonRun.Create(
                runId,
                new RunOptions(
                    new RequestBatchReference("batch"),
                    new EndpointDefinition(new Uri("https://a.example")),
                    new EndpointDefinition(new Uri("https://b.example")),
                    TimeSpan.FromSeconds(30),
                    1));

            string? sample = await CalibrationSampleCapture.CaptureAsync(
                run,
                [new ComparedExecutionRecord(0, result)],
                artifacts,
                root,
                CancellationToken.None);

            Assert.IsNotNull(sample);
            Assert.AreEqual("response-a", await File.ReadAllTextAsync(CalibrationSampleCapture.CapturedArtifactPath(sample, EndpointSlot.A, result.RelativePath)));
            Assert.AreEqual("response-b", await File.ReadAllTextAsync(CalibrationSampleCapture.CapturedArtifactPath(sample, EndpointSlot.B, result.RelativePath)));
            Assert.IsTrue(File.Exists(Path.Combine(sample, CalibrationSampleCapture.ManifestFileName)));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RequestPairResult CreateResult(RunId runId, string relativePath)
    {
        ResponseArtifactMetadata A(EndpointSlot endpoint) => new(
            endpoint,
            new ArtifactReference($"runs/{runId.Value}/artifacts/{endpoint}/{relativePath}", "application/json"),
            200,
            "application/json",
            10,
            new string(endpoint == EndpointSlot.A ? 'a' : 'b', 64));
        return RequestPairResult.Classify(new RequestItem(relativePath), A(EndpointSlot.A), A(EndpointSlot.B));
    }

    private sealed class MemoryArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> artifacts = new(StringComparer.OrdinalIgnoreCase);

        public void Add(string id, string content) => artifacts[id] = Encoding.UTF8.GetBytes(content);

        public Task<bool> ExistsAsync(ArtifactReference artifact, CancellationToken cancellationToken = default) =>
            Task.FromResult(artifacts.ContainsKey(artifact.ArtifactId));

        public Task<Stream> OpenReadAsync(ArtifactReference artifact, CancellationToken cancellationToken = default) =>
            Task.FromResult<Stream>(new MemoryStream(artifacts[artifact.ArtifactId], writable: false));

        public Task<ResponseArtifactMetadata> SaveResponseAsync(RunId runId, EndpointSlot endpoint, RequestItem request, int statusCode, string? contentType, Stream body, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
