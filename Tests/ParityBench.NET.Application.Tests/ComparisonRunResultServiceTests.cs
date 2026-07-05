using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class ComparisonRunResultServiceTests
{
    [TestMethod]
    public async Task ListRuns_WhenCompletedRunsExist_ReturnsSummaryCounts()
    {
        FakeRunStore runStore = new FakeRunStore();
        RunResultSummary summary = CreateSummary(new RunDetailReference("runs/run-1/details/index.json"));
        ComparisonRun run = ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start().Complete(summary);
        await runStore.SaveAsync(run);
        ComparisonRunResultService service = CreateService(runStore);

        IReadOnlyList<RunListItem> runs = await service.ListRunsAsync();

        Assert.AreEqual(1, runs.Count);
        Assert.IsNotNull(runs[0].Summary);
        Assert.AreEqual(3, runs[0].Summary.TotalPairs);
        Assert.AreEqual(1, runs[0].Summary.EqualPairs);
    }

    [TestMethod]
    public async Task LoadRunDetails_WhenQueryUsesPaging_ReturnsExpectedPage()
    {
        RunDetailReference detailReference = new RunDetailReference("runs/run-1/details/index.json");
        FakeRunStore runStore = new FakeRunStore();
        await runStore.SaveAsync(ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start().Complete(CreateSummary(detailReference)));
        FakeRunDetailStore detailStore = new FakeRunDetailStore(new[]
        {
            CreateResult("one.json", RequestPairOutcome.Equal),
            CreateResult("two.json", RequestPairOutcome.Different),
            CreateResult("three.json", RequestPairOutcome.Equal),
        });
        ComparisonRunResultService service = CreateService(runStore, detailStore);

        RunDetailPage page = await service.LoadRunDetailsAsync(new RunId("run-1"), new RunDetailQuery(offset: 1, limit: 1));

        Assert.AreEqual(3, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("two.json", page.Items[0].RelativePath);
        Assert.IsTrue(page.HasMore);
    }

    [TestMethod]
    public async Task LoadRunDetails_WhenOutcomeFilterIsSet_ReturnsOnlyMatchingResults()
    {
        RunDetailReference detailReference = new RunDetailReference("runs/run-1/details/index.json");
        FakeRunStore runStore = new FakeRunStore();
        await runStore.SaveAsync(ComparisonRun.Create(new RunId("run-1"), CreateOptions()).Start().Complete(CreateSummary(detailReference)));
        FakeRunDetailStore detailStore = new FakeRunDetailStore(new[]
        {
            CreateResult("one.json", RequestPairOutcome.Equal),
            CreateResult("two.json", RequestPairOutcome.Different),
            CreateResult("three.json", RequestPairOutcome.Different),
        });
        ComparisonRunResultService service = CreateService(runStore, detailStore);

        RunDetailPage page = await service.LoadRunDetailsAsync(
            new RunId("run-1"),
            new RunDetailQuery(limit: 10, outcome: RequestPairOutcome.Different));

        Assert.AreEqual(2, page.TotalCount);
        CollectionAssert.AreEqual(
            new[] { "two.json", "three.json" },
            page.Items.Select(item => item.RelativePath).ToArray());
    }

    [TestMethod]
    public async Task ReadArtifactPreview_WhenArtifactIsLarge_ReturnsTruncatedPreview()
    {
        ArtifactReference artifact = new ArtifactReference("runs/run-1/artifacts/A/one.txt", "text/plain");
        FakeRunArtifactStore artifactStore = new FakeRunArtifactStore();
        artifactStore.Save(artifact, "abcdef");
        ComparisonRunResultService service = CreateService(artifactStore: artifactStore);

        ArtifactContentPreview preview = await service.ReadArtifactPreviewAsync(artifact, maxBytes: 3);

        Assert.AreEqual("abc", preview.Content);
        Assert.AreEqual(3, preview.BytesRead);
        Assert.IsTrue(preview.IsTruncated);
        Assert.AreEqual(6, preview.TotalLength);
    }

    [TestMethod]
    public async Task ReadArtifactPreview_WhenArtifactDoesNotExist_ThrowsArtifactNotFoundException()
    {
        ComparisonRunResultService service = CreateService();

        await AssertThrowsAsync<ArtifactNotFoundException>(() =>
            service.ReadArtifactPreviewAsync(new ArtifactReference("missing.txt")));
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

    private static ComparisonRunResultService CreateService(
        FakeRunStore? runStore = null,
        FakeRunDetailStore? detailStore = null,
        FakeRunArtifactStore? artifactStore = null) =>
        new ComparisonRunResultService(
            runStore ?? new FakeRunStore(),
            detailStore ?? new FakeRunDetailStore(Array.Empty<RequestPairResult>()),
            artifactStore ?? new FakeRunArtifactStore());

    private static RequestPairResult CreateResult(string relativePath, RequestPairOutcome outcome) =>
        new RequestPairResult(relativePath, outcome);

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            8);

    private static RunResultSummary CreateSummary(RunDetailReference? detailReference = null) =>
        new RunResultSummary(3, 1, 2, 0, detailIndexReference: detailReference);

    private sealed class FakeRunStore : IRunStore
    {
        private readonly Dictionary<RunId, ComparisonRun> runs = new Dictionary<RunId, ComparisonRun>();

        public Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default)
        {
            runs[run.Id] = run;
            return Task.CompletedTask;
        }

        public Task<ComparisonRun?> LoadAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run);
        }

        public Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default)
        {
            IReadOnlyList<RunListItem> items = runs.Values.Select(RunListItem.FromRun).ToList();
            return Task.FromResult(items);
        }

        public Task<RunResultSummary?> LoadSummaryAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            runs.TryGetValue(runId, out ComparisonRun? run);
            return Task.FromResult(run?.Summary);
        }
    }

    private sealed class FakeRunDetailStore : IRunDetailStore
    {
        private readonly IReadOnlyList<RequestPairResult> results;

        public FakeRunDetailStore(IReadOnlyList<RequestPairResult> results)
        {
            this.results = results;
        }

        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(results);

        public Task<RunDetailPage> LoadPageAsync(
            RunDetailReference detailReference,
            RunDetailQuery query,
            CancellationToken cancellationToken = default)
        {
            List<RequestPairResult> matches = results
                .Where(result => query.Outcome is null || result.Outcome == query.Outcome.Value)
                .Where(result => query.RelativePathSearch is null || result.RelativePath.Contains(query.RelativePathSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
            IReadOnlyList<RequestPairResult> pageItems = matches.Skip(query.Offset).Take(query.Limit).ToList();
            return Task.FromResult(new RunDetailPage(pageItems, matches.Count, query.Offset, query.Limit));
        }
    }

    private sealed class FakeRunArtifactStore : IRunArtifactStore
    {
        private readonly Dictionary<string, byte[]> artifacts = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public void Save(ArtifactReference artifact, string content) =>
            artifacts[artifact.ArtifactId] = Encoding.UTF8.GetBytes(content);

        public Task<ResponseArtifactMetadata> SaveResponseAsync(
            RunId runId,
            EndpointSlot endpoint,
            RequestItem request,
            int statusCode,
            string? contentType,
            Stream body,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenReadAsync(ArtifactReference artifact, CancellationToken cancellationToken = default)
        {
            if (!artifacts.TryGetValue(artifact.ArtifactId, out byte[]? content))
            {
                throw new FileNotFoundException("Missing artifact.", artifact.ArtifactId);
            }

            return Task.FromResult<Stream>(new MemoryStream(content));
        }
    }
}