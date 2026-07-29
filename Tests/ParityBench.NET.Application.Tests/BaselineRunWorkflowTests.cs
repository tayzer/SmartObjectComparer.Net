using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Tests;

/// <summary>
/// How a run that captures or replays a baseline is set up: where its requests come
/// from, what the expected side is called, and which mismatches are refused up front.
/// </summary>
[TestClass]
public sealed class BaselineRunWorkflowTests
{
    [TestMethod]
    public async Task CreateRunFromDirectory_WhenReplayingABaseline_StagesTheRequestsStoredInThePackage()
    {
        FakeBaselineStore baselineStore = new FakeBaselineStore(CreateManifest());
        FakeRequestBatchStore batchStore = new FakeRequestBatchStore();
        RequestComparisonWorkflowService service = CreateService(baselineStore, batchStore);

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(CreateReplayRequest());

        // The package owns the scenarios, so the run does not depend on a directory
        // still holding the right request files months later.
        Assert.AreEqual(baselineStore.ExportedDirectory, batchStore.StagedSourceDirectory);
        Assert.AreEqual(BaselineRunMode.BaselineVsLive, run.Options.BaselineMode);
        Assert.AreEqual(new BaselineId("orders"), run.Options.Baseline!.BaselineId);
        Assert.AreEqual(3, run.Options.Baseline.Version);
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenReplayingABaseline_NamesTheExpectedSideAfterThePackage()
    {
        RequestComparisonWorkflowService service = CreateService(new FakeBaselineStore(CreateManifest()));

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(CreateReplayRequest());

        Assert.AreEqual(new Uri("https://legacy.example.test/lookup"), run.Options.EndpointA.Uri);
        StringAssert.Contains(run.Options.EndpointA.Label, "Baseline: Orders upgrade v3");
        StringAssert.Contains(run.Options.EndpointA.Label, "2026-03-01");
        Assert.AreEqual(new Uri("https://b.example.test"), run.Options.EndpointB.Uri);
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenNoVersionIsRequested_ResolvesTheLatest()
    {
        FakeBaselineStore baselineStore = new FakeBaselineStore(CreateManifest());
        RequestComparisonWorkflowService service = CreateService(baselineStore);

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(
            CreateReplayRequest(BaselineRunSelection.Replay(new BaselineId("orders"))));

        Assert.IsNull(baselineStore.RequestedVersion);
        Assert.AreEqual(3, run.Options.Baseline!.Version);
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenTheBaselineWasCapturedForAnotherComparison_IsRefused()
    {
        RequestComparisonWorkflowService service = CreateService(
            new FakeBaselineStore(CreateManifest(comparisonId: "client.lookup.orders")));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.CreateRunFromDirectoryAsync(CreateReplayRequest()));

        StringAssert.Contains(exception.Message, "client.lookup.orders");
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenTheBaselineIsMissing_IsRefused()
    {
        RequestComparisonWorkflowService service = CreateService(new FakeBaselineStore(null));

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.CreateRunFromDirectoryAsync(CreateReplayRequest()));
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenCapturingWithoutAPluginComparison_IsRefused()
    {
        RequestComparisonWorkflowService service = CreateService(new FakeBaselineStore(CreateManifest()));
        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            2,
            baseline: BaselineRunSelection.Capture("Orders upgrade"));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(
            () => service.CreateRunFromDirectoryAsync(request));

        StringAssert.Contains(exception.Message, "plugin comparison");
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenCapturing_CarriesTheCaptureNameOntoTheRun()
    {
        FakeRequestBatchStore batchStore = new FakeRequestBatchStore();
        RequestComparisonWorkflowService service = CreateService(new FakeBaselineStore(CreateManifest()), batchStore);
        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            2,
            pluginComparison: new PluginComparisonSelection("client.lookup", "client.lookup.customer"),
            baseline: BaselineRunSelection.Capture("Orders upgrade"));

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(request);

        // A capture still runs against a directory of requests, and records them.
        Assert.AreEqual("requests", batchStore.StagedSourceDirectory);
        Assert.AreEqual(BaselineRunMode.CaptureBaseline, run.Options.BaselineMode);
        Assert.AreEqual("Orders upgrade", run.Options.Baseline!.CaptureName);
        Assert.AreEqual(EndpointSlot.A, run.Options.Baseline.BaselineSlot);
    }

    private static RequestComparisonRunRequest CreateReplayRequest(BaselineRunSelection? selection = null) =>
        new RequestComparisonRunRequest(
            string.Empty,
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            2,
            pluginComparison: new PluginComparisonSelection("client.lookup", "client.lookup.customer"),
            baseline: selection ?? BaselineRunSelection.Replay(new BaselineId("orders"), 3));

    private static BaselinePackageManifest CreateManifest(string comparisonId = "client.lookup.customer") =>
        new BaselinePackageManifest(
            new BaselineId("orders"),
            "Orders upgrade",
            3,
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            "run-0",
            new Uri("https://legacy.example.test/lookup"),
            "client.lookup",
            comparisonId,
            scenarios: new[]
            {
                new BaselineScenarioEntry("one.xml", "application/xml", 12, 200, "text/xml", "hash", 24),
            });

    private static RequestComparisonWorkflowService CreateService(
        IBaselineStore baselineStore,
        IRequestBatchStore? batchStore = null) =>
        new RequestComparisonWorkflowService(
            batchStore ?? new FakeRequestBatchStore(),
            new FakeRunUseCases(),
            new FakeBatchReferenceGenerator(),
            new FakeReportBundleWriter(),
            new FakeReportAssetLocator(),
            new FakeResponseModelRegistry(),
            retentionOptions: null,
            baselineStore: baselineStore);

    private sealed class FakeBaselineStore : IBaselineStore
    {
        private readonly BaselinePackageManifest? manifest;

        public FakeBaselineStore(BaselinePackageManifest? manifest)
        {
            this.manifest = manifest;
        }

        public int? RequestedVersion { get; private set; }

        public string? ExportedDirectory { get; private set; }

        public Task<BaselinePackageManifest?> LoadManifestAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default)
        {
            RequestedVersion = version;
            return Task.FromResult(manifest);
        }

        public Task<int> ExportRequestsToDirectoryAsync(
            BaselineId id,
            int version,
            string targetDirectory,
            CancellationToken cancellationToken = default)
        {
            ExportedDirectory = targetDirectory;
            return Task.FromResult(1);
        }

        public Task<BaselinePackageManifest> BeginCaptureAsync(
            BaselineCaptureRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BaselineScenarioEntry> AppendScenarioAsync(
            BaselineId id,
            int version,
            BaselineScenarioCapture scenario,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BaselinePackageManifest> CompleteCaptureAsync(
            BaselineId id,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AbandonCaptureAsync(
            BaselineId id,
            int version,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenCanonicalAsync(
            BaselineId id,
            int version,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenRawAsync(
            BaselineId id,
            int version,
            string relativePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task ExportAsync(
            BaselineId id,
            int version,
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<BaselinePackageManifest> ImportAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        public string? StagedSourceDirectory { get; private set; }

        public Task<RequestBatchManifest> StageDirectoryAsync(
            string sourceDirectory,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default)
        {
            StagedSourceDirectory = sourceDirectory;
            return Task.FromResult(new RequestBatchManifest(batchReference, Array.Empty<RequestItem>()));
        }

        public Task<RequestBatchManifest> StageFilesAsync(
            string sourceDirectory,
            IReadOnlyList<string> sourceFiles,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default)
        {
            StagedSourceDirectory = sourceDirectory;
            return Task.FromResult(new RequestBatchManifest(batchReference, Array.Empty<RequestItem>()));
        }

        public Task<RequestBatchManifest> LoadManifestAsync(
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Stream> OpenRequestBodyAsync(
            RequestBatchReference batchReference,
            RequestItem request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeRunUseCases : IComparisonRunUseCases
    {
        public Task<ComparisonRun> CreateRunAsync(RunOptions options, CancellationToken cancellationToken = default) =>
            Task.FromResult(ComparisonRun.Create(new RunId("run-1"), options));

        public Task<ComparisonRun> StartRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> CancelRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBatchReferenceGenerator : IRequestBatchReferenceGenerator
    {
        public RequestBatchReference CreateReference() => new RequestBatchReference("batch-1");
    }

    private sealed class FakeReportBundleWriter : IStaticReportBundleWriter
    {
        public Task<StaticReportBundleWriteResult> WriteAsync(
            RunId runId,
            string outputDirectory,
            string reportAssetsDirectory,
            DateTimeOffset? generatedAt = null,
            int detailPageSize = 100,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new StaticReportBundleWriteResult(outputDirectory, "manifest", "redirect", 1, 0));
    }

    private sealed class FakeReportAssetLocator : IReportAssetLocator
    {
        public string Resolve(string? configuredReportAssetsDirectory = null) => "assets";
    }

    private sealed class FakeResponseModelRegistry : IResponseModelRegistry
    {
        public void Register<T>(string modelName) where T : class
        {
        }

        public Type Resolve(string modelName) => typeof(object);

        public IReadOnlyList<string> ListModelNames() => Array.Empty<string>();
    }
}
