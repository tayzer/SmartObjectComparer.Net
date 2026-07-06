using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor;
using MudBlazor.Services;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Shell;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ParityBenchHomeTests
{
    private BunitContext testContext = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
        testContext.Services.AddSingleton<IRunWorkflowViewDataSource>(new FakeRunWorkflowViewDataSource());
        testContext.Services.AddSingleton<IRunResultsViewDataSource>(new FakeRunResultsViewDataSource());
        testContext.Services.AddSingleton<IRequestSourcePicker>(new NoOpRequestSourcePicker());
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void ParityBenchHome_WhenRendered_ShowsV1LikeHeaderAndTabs()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();

        StringAssert.Contains(component.Markup, "File Comparison Tool (XML &amp; JSON)");
        StringAssert.Contains(component.Markup, "Request Comparison (A/B)");
        StringAssert.Contains(component.Markup, "Run History");
        StringAssert.Contains(component.Markup, "Step 1: Upload Request Files");
    }

    [TestMethod]
    public void ParityBenchHome_WhenFirstRendered_DoesNotShowHistoryAsPrimarySurface()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();

        Assert.IsFalse(component.Markup.Contains("No runs found", StringComparison.Ordinal));
        StringAssert.Contains(component.Markup, "Start Comparison");
    }

    private sealed class FakeRunWorkflowViewDataSource : IRunWorkflowViewDataSource
    {
        public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestComparisonDefaults(
                Array.Empty<ResponseModelOption>(),
                Array.Empty<ContractProfileOption>(),
                Array.Empty<EndpointOption>(),
                Array.Empty<RequestComparisonPresetOption>()));

        public Task<ComparisonRun> CreateRunFromDirectoryAsync(
            RequestComparisonRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StartRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> CancelRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> LoadRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool IsRunning(RunId runId) => false;

        public Task<StaticReportBundleWriteResult> GenerateReportAsync(
            RunId runId,
            string outputDirectory,
            string? reportAssetsDirectory = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRunResultsViewDataSource : IRunResultsViewDataSource
    {
        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunListItem>>(Array.Empty<RunListItem>());

        public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RunResultSummary?>(null);

        public Task<StaticReportMetadata?> LoadReportMetadataAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StaticReportMetadata?>(null);

        public Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult<StaticReportAnalysisSnapshot?>(null);

        public Task<StaticReportDifferenceIndex> LoadDifferenceIndexAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult(new StaticReportDifferenceIndex(0, 0));

        public Task<RunDetailPage> LoadRunDetailsAsync(
            RunId runId,
            RunDetailQuery query,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new RunDetailPage(Array.Empty<RequestPairResult>(), 0, query.Offset, query.Limit));

        public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
            ArtifactReference artifact,
            int maxBytes = 64 * 1024,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ArtifactContentPreview> ReadArtifactContentAsync(
            ArtifactReference artifact,
            int maxBytes = 512 * 1024,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string> ExportRunDetailsJsonAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult("[]");

        public Task<string> ExportRunDetailsCsvAsync(RunId runId, CancellationToken cancellationToken = default) =>
            Task.FromResult("Request,Outcome,Differences");
    }
}
