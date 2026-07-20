using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class ResultLifecycleFitnessTests
{
    [TestMethod]
    public async Task ResultService_WhenRunIsNonTerminal_DoesNotTouchExpensiveDetailStores()
    {
        RunId runId = new RunId("run-1");
        TrackingRunDetailStore detailStore = new TrackingRunDetailStore();
        ComparisonRunResultService service = new ComparisonRunResultService(
            new SingleRunStore(ComparisonRun.Create(runId, CreateOptions()).Start()),
            detailStore,
            new ThrowingRunArtifactStore());

        RunDetailPage page = await service
            .LoadRunDetailsAsync(runId, new RunDetailQuery(limit: 50))
            .ConfigureAwait(false);
        StaticReportAnalysisSnapshot? analysis = await service
            .LoadReportAnalysisAsync(runId)
            .ConfigureAwait(false);
        StaticReportDifferenceIndex? differenceIndex = await service
            .LoadDifferenceIndexAsync(runId)
            .ConfigureAwait(false);

        Assert.AreEqual(0, page.TotalCount);
        Assert.AreEqual(0, page.Items.Count);
        Assert.IsNull(analysis);
        Assert.IsNull(differenceIndex);
        Assert.AreEqual(0, detailStore.LoadPageCount);
        Assert.AreEqual(0, detailStore.LoadAnalysisCount);
        Assert.AreEqual(0, detailStore.LoadDifferenceIndexCount);
    }

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            2);

    private sealed class SingleRunStore : IRunStore
    {
        private readonly ComparisonRun run;

        public SingleRunStore(ComparisonRun run)
        {
            this.run = run;
        }

        public Task SaveAsync(ComparisonRun run, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun?> LoadAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ComparisonRun?>(run.Id == runId ? run : null);

        public Task<IReadOnlyList<RunListItem>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunListItem>>(new[] { RunListItem.FromRun(run) });

        public Task<RunResultSummary?> LoadSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(run.Id == runId ? run.Summary : null);
    }

    private sealed class TrackingRunDetailStore : IRunDetailStore
    {
        public int LoadPageCount { get; private set; }

        public int LoadAnalysisCount { get; private set; }

        public int LoadDifferenceIndexCount { get; private set; }

        public Task<RunDetailReference> SaveDetailsAsync(
            RunId runId,
            IReadOnlyList<RequestPairResult> results,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RequestPairResult>> LoadDetailsAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunDetailPage> LoadPageAsync(
            RunDetailReference detailReference,
            RunDetailQuery query,
            CancellationToken cancellationToken = default)
        {
            LoadPageCount++;
            return Task.FromResult(new RunDetailPage(Array.Empty<RequestPairResult>(), 0, query.Offset, query.Limit));
        }

        public Task<StaticReportAnalysisSnapshot?> LoadAnalysisAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default)
        {
            LoadAnalysisCount++;
            return Task.FromResult<StaticReportAnalysisSnapshot?>(null);
        }

        public Task<StaticReportDifferenceIndex?> LoadDifferenceIndexAsync(
            RunDetailReference detailReference,
            CancellationToken cancellationToken = default)
        {
            LoadDifferenceIndexCount++;
            return Task.FromResult<StaticReportDifferenceIndex?>(null);
        }
    }

    private sealed class ThrowingRunArtifactStore : IRunArtifactStore
    {
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
            throw new NotSupportedException();
    }
}
