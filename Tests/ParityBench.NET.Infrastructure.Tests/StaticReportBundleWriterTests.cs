using System.Text;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Infrastructure.Reports;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class StaticReportBundleWriterTests
{
    private readonly JsonSerializerOptions jsonOptions = StaticReportJsonOptions.Create();

    [TestMethod]
    public async Task WriteAsync_WhenRunHasDetails_WritesManifestPagedDetailsAndRawSidecars()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        FakeRunResults results = CreateResults(CreatePair("one.json", "artifact-a", "artifact-b"));
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("artifact-a", "endpoint-a");
        artifacts.Add("artifact-b", "endpoint-b");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        StaticReportBundleResult result = await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "index.html")));
        Assert.IsTrue(File.Exists(result.ManifestPath));
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "details", "page-000000.json")));
        Assert.AreEqual(1, result.DetailPageCount);
        Assert.AreEqual(2, result.RawArtifactCount);
        Assert.AreEqual(2, Directory.EnumerateFiles(Path.Combine(outputDirectory, "raw")).Count());
    }

    [TestMethod]
    public async Task WriteAsync_WhenRawArtifactIsLarge_DoesNotEmbedBodyInReportData()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        string largeBody = new string('x', 20_000);
        FakeRunResults results = CreateResults(CreatePair("one.json", "artifact-a", "artifact-b"));
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("artifact-a", largeBody);
        artifacts.Add("artifact-b", "small");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        string manifestJson = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "report.data.json"));
        Assert.IsFalse(manifestJson.Contains(largeBody, StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task WriteAsync_WhenDetailCountExceedsPageSize_WritesMultipleDetailPages()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        RequestPairResult[] pairs = Enumerable
            .Range(1, 3)
            .Select(index => CreatePair($"request-{index}.json", $"artifact-a-{index}", $"artifact-b-{index}"))
            .ToArray();
        FakeRunResults results = CreateResults(pairs);
        FakeArtifactStore artifacts = new FakeArtifactStore();
        foreach (RequestPairResult pair in pairs)
        {
            artifacts.Add(pair.ResponseA!.Artifact.ArtifactId, "a");
            artifacts.Add(pair.ResponseB!.Artifact.ArtifactId, "b");
        }

        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        StaticReportBundleResult result = await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory, detailPageSize: 2);

        Assert.AreEqual(2, result.DetailPageCount);
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "details", "page-000000.json")));
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, "details", "page-000001.json")));
    }

    [TestMethod]
    public async Task WriteAsync_WhenReportAssetsAreMissing_ThrowsInvalidOperationException()
    {
        using TempFolder tempFolder = new TempFolder();
        FakeRunResults results = CreateResults(CreatePair("one.json", "artifact-a", "artifact-b"));
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, new FakeArtifactStore());

        await AssertThrowsAsync<InvalidOperationException>(() =>
            writer.WriteAsync(results.Run.Id, Path.Combine(tempFolder.Path, "report"), Path.Combine(tempFolder.Path, "missing")));
    }

    [TestMethod]
    public async Task WriteAsync_WhenArtifactIdsContainUnsafeCharacters_WritesSafeSidecarPaths()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        RequestPairResult pair = CreatePair("one.json", "../unsafe:a", "nested\\unsafe:b");
        FakeRunResults results = CreateResults(pair);
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("../unsafe:a", "a");
        artifacts.Add("nested\\unsafe:b", "b");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        StaticReportDetailPage page = await ReadJsonAsync<StaticReportDetailPage>(Path.Combine(outputDirectory, "details", "page-000000.json"));
        string[] artifactIds = new[]
        {
            page.Items[0].ResponseA!.Artifact.ArtifactId,
            page.Items[0].ResponseB!.Artifact.ArtifactId,
        };

        foreach (string artifactId in artifactIds)
        {
            StringAssert.StartsWith(artifactId, "raw/");
            Assert.IsFalse(artifactId.Contains("..", StringComparison.Ordinal));
            Assert.IsFalse(artifactId.Contains('\\', StringComparison.Ordinal));
            Assert.IsFalse(artifactId.Contains(':', StringComparison.Ordinal));
            Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, artifactId.Replace('/', Path.DirectorySeparatorChar))));
        }
    }

    private async Task<T> ReadJsonAsync<T>(string path)
    {
        await using FileStream stream = File.OpenRead(path);
        return await JsonSerializer.DeserializeAsync<T>(stream, jsonOptions)
            ?? throw new InvalidOperationException($"Could not read {path}.");
    }

    private static FakeRunResults CreateResults(params RequestPairResult[] pairs)
    {
        RunId runId = new RunId("run-1");
        RunResultSummary summary = RequestPairResult.Summarize(
            pairs,
            new RunDetailReference("details/index.json"));
        ComparisonRun run = ComparisonRun.Create(runId, CreateOptions()).Start().Complete(summary);
        return new FakeRunResults(run, pairs);
    }

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2);

    private static RequestPairResult CreatePair(
        string relativePath,
        string artifactA,
        string artifactB) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.Equal,
            new ResponseArtifactMetadata(EndpointSlot.A, new ArtifactReference(artifactA, "text/plain"), 200, "text/plain", 1, "a"),
            new ResponseArtifactMetadata(EndpointSlot.B, new ArtifactReference(artifactB, "text/plain"), 200, "text/plain", 1, "b"));

    private static string CreateAssetsDirectory(TempFolder tempFolder)
    {
        string assetsDirectory = Path.Combine(tempFolder.Path, "assets");
        Directory.CreateDirectory(Path.Combine(assetsDirectory, "_framework"));
        File.WriteAllText(Path.Combine(assetsDirectory, "index.html"), "<html></html>");
        File.WriteAllText(Path.Combine(assetsDirectory, "_framework", "blazor.webassembly.js"), string.Empty);
        return assetsDirectory;
    }
    private static async Task AssertThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    private sealed class FakeRunResults : IComparisonRunResultUseCases
    {
        private readonly IReadOnlyList<RequestPairResult> details;

        public FakeRunResults(
            ComparisonRun run,
            IReadOnlyList<RequestPairResult> details)
        {
            Run = run;
            this.details = details;
        }

        public ComparisonRun Run { get; }

        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunListItem>>(new[] { RunListItem.FromRun(Run) });

        public Task<ComparisonRun> LoadRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Run);

        public Task<RunResultSummary?> LoadRunSummaryAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(Run.Summary);

        public Task<RunDetailPage> LoadRunDetailsAsync(
            RunId runId,
            RunDetailQuery query,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RequestPairResult> items = details
                .Skip(query.Offset)
                .Take(query.Limit)
                .ToList();

            return Task.FromResult(new RunDetailPage(items, details.Count, query.Offset, query.Limit));
        }

        public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
            ArtifactReference artifact,
            int maxBytes = 65536,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public void Add(
            string artifactId,
            string content) =>
            artifacts[artifactId] = Encoding.UTF8.GetBytes(content);

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
            CancellationToken cancellationToken = default)
        {
            byte[] bytes = artifacts[artifact.ArtifactId];
            return Task.FromResult<Stream>(new MemoryStream(bytes, writable: false));
        }
    }

    private sealed class TempFolder : IDisposable
    {
        public TempFolder()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"paritybench-report-tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
