using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Results;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class RunResultsViewTests
{
    private BunitContext testContext = null!;
    private FakeRunResultsViewDataSource dataSource = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
        dataSource = new FakeRunResultsViewDataSource();
        testContext.Services.AddSingleton<IRunResultsViewDataSource>(dataSource);
    }

    [TestCleanup]
    public async Task TearDown()
    {
        if (testContext is not null)
        {
            await testContext.DisposeAsync().ConfigureAwait(false);
        }
    }

    [TestMethod]
    public void RunHistory_WhenRunsAreLoaded_RendersSummaryCounts()
    {
        dataSource.Runs = new[]
        {
            new RunListItem(
                new RunId("run-1"),
                RunStatus.Completed,
                DateTimeOffset.UtcNow.AddMinutes(-10),
                DateTimeOffset.UtcNow,
                new RunProgress(100, "Done"),
                summary: new RunResultSummary(3, 2, 1, 0)),
        };

        IRenderedComponent<RunHistory> component = testContext.Render<RunHistory>();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Equal 2"));
        StringAssert.Contains(component.Markup, "Different 1");
    }

    [TestMethod]
    public void RunResult_WhenRendered_RendersV1StyleReportSurface()
    {
        RunId runId = new RunId("run-1");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = new[] { CreateDifferentPair("one.json") };

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Comparison Report"));
        StringAssert.Contains(component.Markup, "Run Details");
        StringAssert.Contains(component.Markup, "Comparison Results");
        StringAssert.Contains(component.Markup, "Top Affected Objects");
        StringAssert.Contains(component.Markup, "JSON");
        StringAssert.Contains(component.Markup, "CSV");
    }

    [TestMethod]
    public void RunResult_WhenDetailsArePaged_RendersCurrentPageOnly()
    {
        RunId runId = new RunId("run-1");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = Enumerable
            .Range(1, 26)
            .Select(index => CreatePair($"request-{index:00}.json"))
            .ToList();

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "request-25.json"));
        StringAssert.Contains(component.Markup, "Showing 1-25 of 26");
    }

    [TestMethod]
    public void RunResult_WhenPairIsSelected_RendersStructuredDetailWithoutEagerRawRead()
    {
        RunId runId = new RunId("run-1");
        RequestPairResult pair = CreateDifferentPair("one.json");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = new[] { pair };

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Detailed Comparison"));
        StringAssert.Contains(component.Markup, "customer.name");
        Assert.AreEqual(0, dataSource.PreviewReadCount);
    }

    [TestMethod]
    public void RunResult_WhenPairHasRawTextRows_RendersRawDifferenceDetail()
    {
        RunId runId = new RunId("run-1");
        RequestPairResult pair = CreateRawPair("gateway-error.xml");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = new[] { pair };

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Raw Differences"));
        StringAssert.Contains(component.Markup, "StatusCodeDifference");
        StringAssert.Contains(component.Markup, "502");
    }

    [TestMethod]
    public void RunResult_WhenDataSourceFails_ShowsRecoverableError()
    {
        dataSource.ErrorMessage = "Result data source failed.";

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, new RunId("run-1")));

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Result data source failed."));
    }

    private static ComparisonRun CreateCompletedRun(RunId runId)
    {
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 1,
            equalPairs: 1,
            differentPairs: 0,
            errorPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/index.json"));

        return ComparisonRun.Create(runId, CreateOptions()).Start().Complete(summary);
    }

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test"), "Expected"),
            new EndpointDefinition(new Uri("https://service-b.example.test"), "Actual"),
            TimeSpan.FromSeconds(30),
            2);

    private static RequestPairResult CreatePair(string relativePath) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.Equal,
            CreateResponse(EndpointSlot.A, relativePath),
            CreateResponse(EndpointSlot.B, relativePath));

    private static RequestPairResult CreateDifferentPair(string relativePath) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.Different,
            CreateResponse(EndpointSlot.A, relativePath),
            CreateResponse(EndpointSlot.B, relativePath),
            areEqual: false,
            differenceCount: 1,
            differences: new[] { new ComparisonDifference("customer.name", "Alice", "Alicia", "Name changed.") });

    private static RequestPairResult CreateRawPair(string relativePath) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.StatusCodeMismatch,
            CreateResponse(EndpointSlot.A, relativePath, 200),
            CreateResponse(EndpointSlot.B, relativePath, 502),
            areEqual: null,
            differenceCount: 1,
            differences: new[] { new ComparisonDifference("HttpStatus", "200", "502", "Status changed.") },
            outcomeMessage: "Endpoint status mismatch.",
            rawTextDifferences: new[]
            {
                new StaticReportRawTextDifference(StaticReportRawTextDifferenceType.StatusCodeDifference, textA: "200", textB: "502"),
            });

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string relativePath, int statusCode = 200) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference($"runs/run-1/artifacts/{endpoint}/{relativePath}", "text/plain"),
            statusCode,
            "text/plain",
            10,
            "abc");

    private static ArtifactContentPreview CreatePreview(
        ArtifactReference artifact,
        string content,
        bool isTruncated = false) =>
        new ArtifactContentPreview(artifact, content, content.Length, isTruncated, "text/plain", content.Length + (isTruncated ? 10 : 0));

    private sealed class FakeRunResultsViewDataSource : IRunResultsViewDataSource
    {
        public IReadOnlyList<RunListItem> Runs { get; set; } = Array.Empty<RunListItem>();

        public ComparisonRun? Run { get; set; }

        public RunResultSummary? Summary { get; set; }

        public IReadOnlyList<RequestPairResult> Details { get; set; } = Array.Empty<RequestPairResult>();

        public Dictionary<string, ArtifactContentPreview> Previews { get; } = new Dictionary<string, ArtifactContentPreview>(StringComparer.Ordinal);

        public int PreviewReadCount { get; private set; }

        public string? ErrorMessage { get; set; }

        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult(Runs);
        }

        public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult(Run ?? throw new InvalidOperationException("Run was not configured."));
        }

        public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult(Summary);
        }

        public Task<StaticReportMetadata?> LoadReportMetadataAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult<StaticReportMetadata?>(StaticReportMetadata.FromRun(Run ?? throw new InvalidOperationException("Run was not configured."), DateTimeOffset.Parse("2026-01-01T00:00:00Z")));
        }

        public Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            StaticReportAnalysisSnapshot snapshot = new StaticReportAnalysisSnapshot(
                Details.Count,
                Details.Count(detail => detail.Outcome != RequestPairOutcome.ExecutionFailed),
                Details.Count(detail => detail.Outcome != RequestPairOutcome.Equal && detail.Outcome != RequestPairOutcome.ExecutionFailed),
                Details.Count(detail => detail.Outcome == RequestPairOutcome.ExecutionFailed),
                Details.Sum(detail => detail.DifferenceCount),
                new[] { new StaticReportDifferenceCategorySummary("Value Differences", "Value Differences", 1, 1) },
                Details.Where(detail => detail.Outcome != RequestPairOutcome.Equal).Select(detail => new StaticReportAffectedObjectSummary(detail.RelativePath, Math.Max(1, detail.DifferenceCount), "Value Differences", detail.Outcome.ToString())).ToList());
            return Task.FromResult<StaticReportAnalysisSnapshot?>(snapshot);
        }

        public Task<RunDetailPage> LoadRunDetailsAsync(
            RunId runId,
            RunDetailQuery query,
            CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            List<RequestPairResult> matches = Details
                .Where(detail => query.Outcome is null || detail.Outcome == query.Outcome.Value)
                .Where(detail => query.RelativePathSearch is null || detail.RelativePath.Contains(query.RelativePathSearch, StringComparison.OrdinalIgnoreCase))
                .ToList();
            IReadOnlyList<RequestPairResult> items = matches.Skip(query.Offset).Take(query.Limit).ToList();
            return Task.FromResult(new RunDetailPage(items, matches.Count, query.Offset, query.Limit));
        }

        public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
            ArtifactReference artifact,
            int maxBytes = 64 * 1024,
            CancellationToken cancellationToken = default) =>
            ReadArtifactContentAsync(artifact, maxBytes, cancellationToken);

        public Task<ArtifactContentPreview> ReadArtifactContentAsync(
            ArtifactReference artifact,
            int maxBytes = 512 * 1024,
            CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            PreviewReadCount++;
            return Task.FromResult(Previews[artifact.ArtifactId]);
        }

        public Task<string> ExportRunDetailsJsonAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult("[]");
        }

        public Task<string> ExportRunDetailsCsvAsync(RunId runId, CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            return Task.FromResult("Request,Outcome,Differences");
        }

        private void ThrowIfConfigured()
        {
            if (ErrorMessage is not null)
            {
                throw new InvalidOperationException(ErrorMessage);
            }
        }
    }
}
