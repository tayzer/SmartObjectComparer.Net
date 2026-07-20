using System.Net;
using System.Text;
using System.Text.Json;

using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.AcceptedDifferences;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Report;
using ParityBench.NET.Report.Results;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Theming;

namespace ParityBench.NET.Report.Tests;

[TestClass]
public sealed class StaticReportRunResultsViewDataSourceTests
{
    private readonly JsonSerializerOptions jsonOptions = StaticReportJsonOptions.Create();

    [TestMethod]
    public async Task LoadRuns_WhenReportDataExists_ReturnsSingleRun()
    {
        TestReportData reportData = CreateReportData(CreatePair("one.json", RequestPairOutcome.Equal));
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        IReadOnlyList<RunListItem> runs = await dataSource.ListRunsAsync();

        Assert.AreEqual(1, runs.Count);
        Assert.AreEqual("run-1", runs[0].Id.Value);
        Assert.AreEqual(1, runs[0].Summary?.TotalPairs);
    }

    [TestMethod]
    public async Task LoadRunDetails_WhenPageExists_ReturnsRequestedPage()
    {
        TestReportData reportData = CreateReportData(
            CreatePair("one.json", RequestPairOutcome.Equal),
            CreatePair("two.json", RequestPairOutcome.Different));
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        RunDetailPage page = await dataSource.LoadRunDetailsAsync(new RunId("run-1"), new RunDetailQuery(offset: 1, limit: 1));

        Assert.AreEqual(2, page.TotalCount);
        Assert.AreEqual(1, page.Items.Count);
        Assert.AreEqual("two.json", page.Items[0].RelativePath);
    }

    [TestMethod]
    public async Task LoadRunDetails_WhenFilterIsUsed_ReturnsMatchingDetails()
    {
        TestReportData reportData = CreateReportData(
            CreatePair("equal.json", RequestPairOutcome.Equal),
            CreatePair("different.json", RequestPairOutcome.Different));
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        RunDetailPage page = await dataSource.LoadRunDetailsAsync(
            new RunId("run-1"),
            new RunDetailQuery(outcome: RequestPairOutcome.Different, relativePathSearch: "different"));

        Assert.AreEqual(1, page.TotalCount);
        Assert.AreEqual("different.json", page.Items[0].RelativePath);
    }

    [TestMethod]
    public async Task ReadArtifactPreview_WhenRawSidecarIsLarge_ReturnsTruncatedPreview()
    {
        TestReportData reportData = CreateReportData(CreatePair("one.json", RequestPairOutcome.Equal));
        reportData.Files["raw/a.body"] = Encoding.UTF8.GetBytes("abcdef");
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        ArtifactContentPreview preview = await dataSource.ReadArtifactPreviewAsync(
            new ArtifactReference("raw/a.body", "text/plain"),
            maxBytes: 3);

        Assert.AreEqual("abc", preview.Content);
        Assert.AreEqual(3, preview.BytesRead);
        Assert.IsTrue(preview.IsTruncated);
    }

    [TestMethod]
    public async Task ReportRoot_WhenDataLoads_RendersSharedResultSurface()
    {
        BunitContext testContext = new BunitContext();
        try
        {
            testContext.JSInterop.Mode = JSRuntimeMode.Loose;
            testContext.Services.AddMudServices();
            testContext.Services.AddScoped<ParityBenchThemeState>();
            StaticReportRunResultsViewDataSource dataSource = CreateDataSource(CreateReportData(CreatePair("one.json", RequestPairOutcome.Equal)));
            testContext.Services.AddSingleton<IRunResultsViewDataSource>(dataSource);
            testContext.Services.AddSingleton<IAcceptedDifferenceUseCases>(new InMemoryAcceptedDifferenceUseCases(isReadOnly: false));

            IRenderedComponent<ReportRoot> component = testContext.Render<ReportRoot>();

            component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Run run-1"));
            StringAssert.Contains(component.Markup, "one.json");
        }
        finally
        {
            await testContext.DisposeAsync().ConfigureAwait(false);
        }
    }


    [TestMethod]
    public async Task LoadDifferenceIndex_WhenSidecarExists_ReturnsPackagedIndex()
    {
        RequestPairResult pair = CreateDifferentPair("customers/one.json");
        TestReportData reportData = CreateReportData(pair);
        StaticReportDifferenceIndex packagedIndex = new StaticReportDifferenceIndex(
            1,
            1,
            new[]
            {
                new StaticReportPropertyDifferenceSummary(
                    "Customer.Name",
                    "Customer.Name",
                    "Value Differences",
                    1,
                    1,
                    new[] { new StaticReportAffectedPairDifference("customers/one.json", "Customer.Name", "Customer.Name", "Value Differences", 1, RequestPairOutcome.Different) }),
            });
        reportData.Manifest = CreateManifest(reportData.Pages[0].Items, "analysis/difference-index.json");
        reportData.Files["analysis/difference-index.json"] = JsonSerializer.SerializeToUtf8Bytes(packagedIndex, jsonOptions);
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        StaticReportDifferenceIndex index = await dataSource.LoadDifferenceIndexAsync(new RunId("run-1"));

        Assert.AreEqual(1, index.TotalDifferences);
        Assert.AreEqual("Customer.Name", index.Properties[0].NormalizedPath);
    }

    [TestMethod]
    public async Task LoadDifferenceIndex_WhenSidecarIsMissing_FallsBackToDetailPages()
    {
        RequestPairResult pair = CreateDifferentPair("customers/one.json");
        TestReportData reportData = CreateReportData(pair);
        reportData.Manifest = CreateManifest(reportData.Pages[0].Items, "analysis/missing.json");
        StaticReportRunResultsViewDataSource dataSource = CreateDataSource(reportData);

        StaticReportDifferenceIndex index = await dataSource.LoadDifferenceIndexAsync(new RunId("run-1"));

        Assert.AreEqual(1, index.TotalDifferences);
        Assert.AreEqual("Customer.Name", index.Properties[0].NormalizedPath);
        Assert.AreEqual("customers/one.json", index.Properties[0].AffectedPairs[0].RelativePath);
    }

    private StaticReportRunResultsViewDataSource CreateDataSource(TestReportData reportData)
    {
        Dictionary<string, byte[]> files = new Dictionary<string, byte[]>(reportData.Files, StringComparer.OrdinalIgnoreCase)
        {
            ["report.data.json"] = JsonSerializer.SerializeToUtf8Bytes(reportData.Manifest, jsonOptions),
        };

        foreach (StaticReportDetailPage page in reportData.Pages)
        {
            string path = $"details/page-{page.PageIndex:000000}.json";
            files[path] = JsonSerializer.SerializeToUtf8Bytes(page, jsonOptions);
        }

        HttpClient httpClient = new HttpClient(new StaticFileHandler(files))
        {
            BaseAddress = new Uri("https://report.example.test/"),
        };

        return new StaticReportRunResultsViewDataSource(httpClient);
    }

    private TestReportData CreateReportData(params RequestPairResult[] pairs)
    {
        RunId runId = new RunId("run-1");
        RunResultSummary summary = RequestPairResult.Summarize(pairs, new RunDetailReference("details"));
        ComparisonRun run = ComparisonRun.Create(runId, CreateOptions()).Start().Complete(summary);
        StaticReportDetailPage page = new StaticReportDetailPage(0, 0, pairs.Length, pairs);
        StaticReportManifest manifest = new StaticReportManifest(
            StaticReportManifest.CurrentSchemaVersion,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StaticReportRunSnapshot.FromRun(run),
            summary,
            StaticReportManifest.DefaultDetailPageSize,
            new[]
            {
                new StaticReportDetailPageInfo(0, 0, pairs.Length, "details/page-000000.json"),
            });

        TestReportData reportData = new TestReportData(manifest, new[] { page });
        reportData.Files["raw/a.body"] = Encoding.UTF8.GetBytes("endpoint-a");
        reportData.Files["raw/b.body"] = Encoding.UTF8.GetBytes("endpoint-b");
        return reportData;
    }


    private static StaticReportManifest CreateManifest(IReadOnlyList<RequestPairResult> pairs, string? differenceIndexPath = null)
    {
        RunId runId = new RunId("run-1");
        RunResultSummary summary = RequestPairResult.Summarize(pairs, new RunDetailReference("details"));
        ComparisonRun run = ComparisonRun.Create(runId, CreateOptions()).Start().Complete(summary);
        StaticReportAnalysisSnapshot? analysis = differenceIndexPath is null
            ? null
            : new StaticReportAnalysisSnapshot(
                pairs.Count,
                pairs.Count(pair => pair.Outcome != RequestPairOutcome.ExecutionFailed),
                pairs.Count(pair => pair.Outcome != RequestPairOutcome.Equal && pair.Outcome != RequestPairOutcome.ExecutionFailed),
                pairs.Count(pair => pair.Outcome == RequestPairOutcome.ExecutionFailed),
                pairs.Sum(pair => pair.DifferenceCount),
                differenceIndexPath: differenceIndexPath);
        return new StaticReportManifest(
            StaticReportManifest.CurrentSchemaVersion,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z"),
            StaticReportRunSnapshot.FromRun(run),
            summary,
            StaticReportManifest.DefaultDetailPageSize,
            new[]
            {
                new StaticReportDetailPageInfo(0, 0, pairs.Count, "details/page-000000.json"),
            },
            analysis: analysis);
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
        RequestPairOutcome outcome) =>
        new RequestPairResult(
            relativePath,
            outcome,
            new ResponseArtifactMetadata(EndpointSlot.A, new ArtifactReference("raw/a.body", "text/plain"), 200, "text/plain", 10, "a"),
            new ResponseArtifactMetadata(EndpointSlot.B, new ArtifactReference("raw/b.body", "text/plain"), 200, "text/plain", 10, "b"),
            areEqual: outcome == RequestPairOutcome.Equal,
            differenceCount: outcome == RequestPairOutcome.Equal ? 0 : 1);


    private static RequestPairResult CreateDifferentPair(string relativePath) =>
        new RequestPairResult(
            relativePath,
            RequestPairOutcome.Different,
            new ResponseArtifactMetadata(EndpointSlot.A, new ArtifactReference("raw/a.body", "text/plain"), 200, "text/plain", 10, "a"),
            new ResponseArtifactMetadata(EndpointSlot.B, new ArtifactReference("raw/b.body", "text/plain"), 200, "text/plain", 10, "b"),
            areEqual: false,
            differenceCount: 1,
            differences: new[] { new ComparisonDifference("Customer.Name", "Alice", "Alicia", "Name changed.") });
    private sealed class TestReportData
    {
        public TestReportData(
            StaticReportManifest manifest,
            IReadOnlyList<StaticReportDetailPage> pages)
        {
            Manifest = manifest;
            Pages = pages;
        }

        public StaticReportManifest Manifest { get; set; }

        public IReadOnlyList<StaticReportDetailPage> Pages { get; }

        public Dictionary<string, byte[]> Files { get; } = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class StaticFileHandler : HttpMessageHandler
    {
        private readonly IReadOnlyDictionary<string, byte[]> files;

        public StaticFileHandler(IReadOnlyDictionary<string, byte[]> files)
        {
            this.files = files;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath.TrimStart('/') ?? string.Empty;
            if (!files.TryGetValue(path, out byte[]? bytes))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(bytes),
            });
        }
    }
}
