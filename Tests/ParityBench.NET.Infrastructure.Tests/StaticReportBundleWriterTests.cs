using System.Text;
using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
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


    [TestMethod]
    public async Task WriteAsync_WhenRunHasReportDifferences_WritesV2MetadataAnalysisAndRawRows()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        RequestPairResult pair = new RequestPairResult(
            "status.xml",
            RequestPairOutcome.StatusCodeMismatch,
            CreateResponse(EndpointSlot.A, "artifact-a", 200),
            CreateResponse(EndpointSlot.B, "artifact-b", 502),
            areEqual: null,
            differenceCount: 2,
            differences: new[]
            {
                new ComparisonDifference("HttpStatus", "200", "502", "Status changed."),
                new ComparisonDifference("Body.Line[1]", "ok", "bad gateway", "Raw response body line 1 differs."),
            },
            outcomeMessage: "Endpoint status mismatch.");
        FakeRunResults results = CreateResults(pair);
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("artifact-a", "ok");
        artifacts.Add("artifact-b", "bad gateway");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        StaticReportManifest manifest = await ReadJsonAsync<StaticReportManifest>(Path.Combine(outputDirectory, "report.data.json"));
        StaticReportDetailPage page = await ReadJsonAsync<StaticReportDetailPage>(Path.Combine(outputDirectory, "details", "page-000000.json"));

        Assert.AreEqual(StaticReportManifest.CurrentSchemaVersion, manifest.SchemaVersion);
        Assert.IsNotNull(manifest.Metadata);
        Assert.AreEqual("run-1", manifest.Metadata.RunId);
        Assert.IsNotNull(manifest.Analysis);
        Assert.IsTrue(manifest.Analysis.Categories.Any(category => category.Category == "HTTP Status"));
        Assert.IsTrue(manifest.Analysis.TopAffectedObjects.Any(item => item.Identifier == "status.xml"));
        Assert.AreEqual(2, page.Items[0].RawTextDifferences.Count);
        Assert.AreEqual(StaticReportRawTextDifferenceType.StatusCodeDifference, page.Items[0].RawTextDifferences[0].Type);
    }

    [TestMethod]
    public async Task WriteAsync_WhenPairHasFocusedArtifacts_RewritesFocusedSidecars()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        RequestPairResult pair = new RequestPairResult(
            "one.json",
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, "artifact-a", 200),
            CreateResponse(EndpointSlot.B, "artifact-b", 200),
            areEqual: false,
            differenceCount: 1,
            differences: new[] { new ComparisonDifference("Name", "Alice", "Alicia", "Name changed.") },
            focusedResponseA: CreateResponse(EndpointSlot.A, "focused-a", 200),
            focusedResponseB: CreateResponse(EndpointSlot.B, "focused-b", 200),
            focusedRawContentIgnorePaths: new[] { "Customer.Token" });
        FakeRunResults results = CreateResults(pair);
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("artifact-a", "full-a");
        artifacts.Add("artifact-b", "full-b");
        artifacts.Add("focused-a", "focused-a-body");
        artifacts.Add("focused-b", "focused-b-body");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        StaticReportBundleResult result = await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        StaticReportDetailPage page = await ReadJsonAsync<StaticReportDetailPage>(Path.Combine(outputDirectory, "details", "page-000000.json"));
        RequestPairResult rewritten = page.Items[0];
        Assert.AreEqual(4, result.RawArtifactCount);
        Assert.IsTrue(rewritten.HasFocusedRawContent);
        StringAssert.StartsWith(rewritten.FocusedResponseA!.Artifact.ArtifactId, "raw/");
        StringAssert.StartsWith(rewritten.FocusedResponseB!.Artifact.ArtifactId, "raw/");
        CollectionAssert.Contains(rewritten.FocusedRawContentIgnorePaths.ToList(), "Customer.Token");
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, rewritten.FocusedResponseA.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar))));
        Assert.IsTrue(File.Exists(Path.Combine(outputDirectory, rewritten.FocusedResponseB.Artifact.ArtifactId.Replace('/', Path.DirectorySeparatorChar))));
    }




    [TestMethod]
    public async Task WriteAsync_WhenRunHasStructuredDifferences_WritesDifferenceIndexSidecar()
    {
        using TempFolder tempFolder = new TempFolder();
        string assetsDirectory = CreateAssetsDirectory(tempFolder);
        string outputDirectory = Path.Combine(tempFolder.Path, "report");
        RequestPairResult pair = new RequestPairResult(
            "customers/one.json",
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, "artifact-a", 200),
            CreateResponse(EndpointSlot.B, "artifact-b", 200),
            areEqual: false,
            differenceCount: 4,
            differences: new[]
            {
                new ComparisonDifference("Customer.Addresses[0].City", "London", "Paris", "City changed."),
                new ComparisonDifference("Customer.Addresses[1].City", "York", "Lyon", "City changed."),
                new ComparisonDifference("Subject.ContactProfile.NotificationPreference.StatementDelivery", "Postal", "Email", "Statement delivery preference changed."),
                new ComparisonDifference("Subject.ContactProfile.NotificationPreference.MarketingConsent", "Accepted", "Declined", "Marketing consent changed."),
            });
        FakeRunResults results = CreateResults(pair);
        FakeArtifactStore artifacts = new FakeArtifactStore();
        artifacts.Add("artifact-a", new string('a', 20000));
        artifacts.Add("artifact-b", "small");
        StaticReportBundleWriter writer = new StaticReportBundleWriter(results, artifacts);

        await writer.WriteAsync(results.Run.Id, outputDirectory, assetsDirectory);

        StaticReportManifest manifest = await ReadJsonAsync<StaticReportManifest>(Path.Combine(outputDirectory, "report.data.json"));
        StaticReportDifferenceIndex index = await ReadJsonAsync<StaticReportDifferenceIndex>(Path.Combine(outputDirectory, "analysis", "difference-index.json"));
        string indexJson = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "analysis", "difference-index.json"));

        Assert.AreEqual("analysis/difference-index.json", manifest.Analysis?.DifferenceIndexPath);
        Assert.AreEqual(4, index.TotalDifferences);
        Assert.AreEqual(1, index.AffectedPairCount);
        List<string> normalizedPaths = index.Properties.Select(property => property.NormalizedPath).ToList();
        CollectionAssert.Contains(normalizedPaths, "Customer.Addresses[*].City");
        CollectionAssert.Contains(normalizedPaths, "Subject.ContactProfile.NotificationPreference.StatementDelivery");
        CollectionAssert.Contains(normalizedPaths, "Subject.ContactProfile.NotificationPreference.MarketingConsent");
        Assert.IsTrue(index.Properties.All(property => property.AffectedPairs.Any(pair => pair.RelativePath == "customers/one.json")));
        Assert.IsFalse(indexJson.Contains(new string('a', 20000), StringComparison.Ordinal));
    }

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string artifactId, int statusCode) =>
        new ResponseArtifactMetadata(endpoint, new ArtifactReference(artifactId, "text/plain"), statusCode, "text/plain", 1, artifactId);

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

