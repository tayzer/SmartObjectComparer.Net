using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Results;
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
        Assert.IsFalse(component.Markup.Contains("request-26.json", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RunResult_WhenPairIsSelected_LoadsRawPreviewOnDemand()
    {
        RunId runId = new RunId("run-1");
        RequestPairResult pair = CreatePair("one.json");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = new[] { pair };
        dataSource.Previews[pair.ResponseA!.Artifact.ArtifactId] = CreatePreview(pair.ResponseA.Artifact, "endpoint-a");
        dataSource.Previews[pair.ResponseB!.Artifact.ArtifactId] = CreatePreview(pair.ResponseB.Artifact, "endpoint-b");

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));
        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "one.json"));
        Assert.AreEqual(0, dataSource.PreviewReadCount);

        component.FindAll("button").Single(button => button.TextContent.Contains("one.json", StringComparison.Ordinal)).Click();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "endpoint-a"));
        StringAssert.Contains(component.Markup, "endpoint-b");
        Assert.AreEqual(2, dataSource.PreviewReadCount);
    }

    [TestMethod]
    public void RunResult_WhenPreviewIsTruncated_ShowsTruncationState()
    {
        RunId runId = new RunId("run-1");
        RequestPairResult pair = CreatePair("one.json");
        dataSource.Run = CreateCompletedRun(runId);
        dataSource.Summary = dataSource.Run.Summary;
        dataSource.Details = new[] { pair };
        dataSource.Previews[pair.ResponseA!.Artifact.ArtifactId] = CreatePreview(pair.ResponseA.Artifact, "truncated", isTruncated: true);
        dataSource.Previews[pair.ResponseB!.Artifact.ArtifactId] = CreatePreview(pair.ResponseB.Artifact, "full");

        IRenderedComponent<RunResult> component = testContext.Render<RunResult>(parameters =>
            parameters.Add(result => result.RunId, runId));
        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "one.json"));

        component.FindAll("button").Single(button => button.TextContent.Contains("one.json", StringComparison.Ordinal)).Click();

        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Preview truncated"));
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
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2);

    private static RequestPairResult CreatePair(string relativePath) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.Equal,
            CreateResponse(EndpointSlot.A, relativePath),
            CreateResponse(EndpointSlot.B, relativePath));

    private static ResponseArtifactMetadata CreateResponse(EndpointSlot endpoint, string relativePath) =>
        new ResponseArtifactMetadata(
            endpoint,
            new ArtifactReference($"runs/run-1/artifacts/{endpoint}/{relativePath}", "text/plain"),
            200,
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
            CancellationToken cancellationToken = default)
        {
            ThrowIfConfigured();
            PreviewReadCount++;
            return Task.FromResult(Previews[artifact.ArtifactId]);
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