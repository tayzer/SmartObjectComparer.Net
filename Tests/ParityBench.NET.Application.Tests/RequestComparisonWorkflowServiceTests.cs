using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Tests;

[TestClass]
public sealed class RequestComparisonWorkflowServiceTests
{
    [TestMethod]
    public async Task CreateRunFromDirectory_WhenRequestIsValid_StagesBatchAndPersistsCreatedRun()
    {
        FakeRequestBatchStore batchStore = new FakeRequestBatchStore();
        FakeRunUseCases runUseCases = new FakeRunUseCases();
        RequestComparisonWorkflowService service = CreateService(batchStore, runUseCases);
        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            3,
            commonHeaders: new Dictionary<string, string> { ["X-Common"] = "shared" },
            endpointAHeaders: new Dictionary<string, string> { ["X-A"] = "a" });

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(request).ConfigureAwait(false);

        Assert.AreEqual("requests", batchStore.StagedSourceDirectory);
        Assert.AreEqual(new RequestBatchReference("batch-1"), batchStore.StagedBatchReference);
        Assert.IsNotNull(runUseCases.CreatedOptions);
        Assert.AreEqual(run.Options, runUseCases.CreatedOptions);
        Assert.AreEqual("shared", run.Options.EndpointA.Headers["X-Common"]);
        Assert.AreEqual("a", run.Options.EndpointA.Headers["X-A"]);
        Assert.AreEqual("shared", run.Options.EndpointB.Headers["X-Common"]);
        Assert.IsFalse(run.Options.EndpointB.Headers.ContainsKey("X-A"));
        Assert.IsFalse(string.IsNullOrWhiteSpace(run.Options.ComparisonRulesSnapshotHash));
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenRunRetentionOverrideIsProvided_PersistsRunOptionsOverride()
    {
        FakeRunUseCases runUseCases = new FakeRunUseCases();
        RequestComparisonWorkflowService service = CreateService(runUseCases: runUseCases);
        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            3,
            runRetentionModeOverride: RetentionMode.None);

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(request).ConfigureAwait(false);

        Assert.AreEqual(RetentionMode.None, run.Options.RunRetentionModeOverride);
        Assert.AreEqual(RetentionMode.None, runUseCases.CreatedOptions?.RunRetentionModeOverride);
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenLargeRunOptionsAreProvided_PersistsComparisonConcurrency()
    {
        FakeRunUseCases runUseCases = new();
        RequestComparisonWorkflowService service = CreateService(runUseCases: runUseCases);
        RequestComparisonRunRequest request = new(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            3,
            largeRunOptions: new LargeRunOptions(comparisonConcurrency: 8));

        ComparisonRun run = await service.CreateRunFromDirectoryAsync(request).ConfigureAwait(false);

        Assert.AreEqual(8, request.LargeRunOptions.ComparisonConcurrency);
        Assert.AreEqual(8, run.Options.LargeRun.ComparisonConcurrency);
        Assert.AreEqual(8, runUseCases.CreatedOptions?.LargeRun.ComparisonConcurrency);
    }

    [TestMethod]
    public async Task CreateRunFromDirectory_WhenSourceFilesAreProvided_StagesExplicitFiles()
    {
        FakeRequestBatchStore batchStore = new FakeRequestBatchStore();
        RequestComparisonWorkflowService service = CreateService(batchStore);
        string[] sourceFiles = new[] { "requests/one.json", "requests/two.xml" };
        RequestComparisonRunRequest request = new RequestComparisonRunRequest(
            "requests",
            new Uri("https://a.example.test"),
            new Uri("https://b.example.test"),
            TimeSpan.FromSeconds(30),
            3,
            sourceFiles: sourceFiles);

        await service.CreateRunFromDirectoryAsync(request).ConfigureAwait(false);

        Assert.AreEqual("requests", batchStore.StagedSourceDirectory);
        CollectionAssert.AreEqual(sourceFiles, batchStore.StagedSourceFiles.ToArray());
        Assert.AreEqual(new RequestBatchReference("batch-1"), batchStore.StagedBatchReference);
    }
    [TestMethod]
    public async Task GenerateReport_WhenRunIsCompleted_CallsStaticReportWriterWithResolvedAssets()
    {
        FakeReportAssetLocator locator = new FakeReportAssetLocator("resolved-assets");
        FakeReportBundleWriter writer = new FakeReportBundleWriter();
        RequestComparisonWorkflowService service = CreateService(reportAssetLocator: locator, reportBundleWriter: writer);
        RunId runId = new RunId("run-1");

        StaticReportBundleWriteResult result = await service
            .GenerateReportAsync(runId, "output", "configured-assets")
            .ConfigureAwait(false);

        Assert.AreEqual("configured-assets", locator.ConfiguredDirectory);
        Assert.AreEqual(runId, writer.RunId);
        Assert.AreEqual("output", writer.OutputDirectory);
        Assert.AreEqual("resolved-assets", writer.ReportAssetsDirectory);
        Assert.AreEqual("output", result.OutputDirectory);
    }

    [TestMethod]
    public async Task StartRun_WhenRunIsAlreadyRunning_DoesNotStartDuplicateJob()
    {
        BlockingWorkflowUseCases workflow = new BlockingWorkflowUseCases();
        ComparisonRunJobService service = new ComparisonRunJobService(workflow);
        RunId runId = new RunId("run-1");

        bool firstStart = await service.StartRunAsync(runId).ConfigureAwait(false);
        await workflow.Started.Task.ConfigureAwait(false);
        bool secondStart = await service.StartRunAsync(runId).ConfigureAwait(false);
        workflow.Release.SetResult();

        Assert.IsTrue(firstStart);
        Assert.IsFalse(secondStart);
        Assert.AreEqual(1, workflow.StartCount);
    }

    [TestMethod]
    public async Task CancelRun_WhenBackgroundRunIsActive_RequestsCancellationAndPersistsCancelledRun()
    {
        BlockingWorkflowUseCases workflow = new BlockingWorkflowUseCases();
        ComparisonRunJobService service = new ComparisonRunJobService(workflow);
        RunId runId = new RunId("run-1");

        await service.StartRunAsync(runId).ConfigureAwait(false);
        await workflow.Started.Task.ConfigureAwait(false);
        ComparisonRun cancelledRun = await service.CancelRunAsync(runId).ConfigureAwait(false);
        workflow.Release.SetResult();

        Assert.AreEqual(RunStatus.Cancelled, cancelledRun.Status);
        Assert.AreEqual(runId, workflow.CancelledRunId);
    }

    private static RequestComparisonWorkflowService CreateService(
        IRequestBatchStore? requestBatchStore = null,
        IComparisonRunUseCases? runUseCases = null,
        IRequestBatchReferenceGenerator? requestBatchReferenceGenerator = null,
        IStaticReportBundleWriter? reportBundleWriter = null,
        IReportAssetLocator? reportAssetLocator = null,
        IResponseModelRegistry? responseModelRegistry = null) =>
        new RequestComparisonWorkflowService(
            requestBatchStore ?? new FakeRequestBatchStore(),
            runUseCases ?? new FakeRunUseCases(),
            requestBatchReferenceGenerator ?? new FakeBatchReferenceGenerator(),
            reportBundleWriter ?? new FakeReportBundleWriter(),
            reportAssetLocator ?? new FakeReportAssetLocator("assets"),
            responseModelRegistry ?? new FakeResponseModelRegistry());

    private sealed class FakeRequestBatchStore : IRequestBatchStore
    {
        public string? StagedSourceDirectory { get; private set; }

        public RequestBatchReference? StagedBatchReference { get; private set; }

        public IReadOnlyList<string> StagedSourceFiles { get; private set; } = Array.Empty<string>();

        public Task<RequestBatchManifest> StageDirectoryAsync(
            string sourceDirectory,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default)
        {
            StagedSourceDirectory = sourceDirectory;
            StagedBatchReference = batchReference;
            return Task.FromResult(new RequestBatchManifest(batchReference, Array.Empty<RequestItem>()));
        }

        public Task<RequestBatchManifest> StageFilesAsync(
            string sourceDirectory,
            IReadOnlyList<string> sourceFiles,
            RequestBatchReference batchReference,
            CancellationToken cancellationToken = default)
        {
            StagedSourceDirectory = sourceDirectory;
            StagedSourceFiles = sourceFiles;
            StagedBatchReference = batchReference;
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
        public RunOptions? CreatedOptions { get; private set; }

        public Task<ComparisonRun> CreateRunAsync(
            RunOptions options,
            CancellationToken cancellationToken = default)
        {
            CreatedOptions = options;
            return Task.FromResult(ComparisonRun.Create(new RunId("run-1"), options));
        }

        public Task<ComparisonRun> StartRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> CancelRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RunResultSummary?> LoadRunSummaryAsync(
            RunId runId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FakeBatchReferenceGenerator : IRequestBatchReferenceGenerator
    {
        public RequestBatchReference CreateReference() => new RequestBatchReference("batch-1");
    }

    private sealed class FakeReportBundleWriter : IStaticReportBundleWriter
    {
        public RunId RunId { get; private set; }

        public string? OutputDirectory { get; private set; }

        public string? ReportAssetsDirectory { get; private set; }

        public Task<StaticReportBundleWriteResult> WriteAsync(
            RunId runId,
            string outputDirectory,
            string reportAssetsDirectory,
            DateTimeOffset? generatedAt = null,
            int detailPageSize = 100,
            CancellationToken cancellationToken = default)
        {
            RunId = runId;
            OutputDirectory = outputDirectory;
            ReportAssetsDirectory = reportAssetsDirectory;
            return Task.FromResult(new StaticReportBundleWriteResult(outputDirectory, "manifest", "redirect", 1, 0));
        }
    }

    private sealed class FakeReportAssetLocator : IReportAssetLocator
    {
        private readonly string resolvedDirectory;

        public FakeReportAssetLocator(string resolvedDirectory)
        {
            this.resolvedDirectory = resolvedDirectory;
        }

        public string? ConfiguredDirectory { get; private set; }

        public string Resolve(string? configuredReportAssetsDirectory = null)
        {
            ConfiguredDirectory = configuredReportAssetsDirectory;
            return resolvedDirectory;
        }
    }

    private sealed class FakeResponseModelRegistry : IResponseModelRegistry
    {
        public void Register<T>(string modelName) where T : class
        {
        }

        public Type Resolve(string modelName) => typeof(object);

        public IReadOnlyList<string> ListModelNames() => Array.Empty<string>();
    }

    private sealed class BlockingWorkflowUseCases : IRequestComparisonWorkflowUseCases
    {
        private readonly RunOptions options = new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            1);

        public TaskCompletionSource Started { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public int StartCount { get; private set; }

        public RunId? CancelledRunId { get; private set; }

        public Task<ComparisonRun> CreateRunFromDirectoryAsync(
            RequestComparisonRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public async Task<ComparisonRun> StartRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default)
        {
            StartCount++;
            Started.TrySetResult();
            await Release.Task.ConfigureAwait(false);
            return ComparisonRun.Create(runId, options).Start().Complete(new RunResultSummary(0, 0, 0, 0));
        }

        public Task<ComparisonRun> CancelRunAsync(
            RunId runId,
            CancellationToken cancellationToken = default)
        {
            CancelledRunId = runId;
            return Task.FromResult(ComparisonRun.Create(runId, options).Cancel());
        }

        public Task<StaticReportBundleWriteResult> GenerateReportAsync(
            RunId runId,
            string outputDirectory,
            string? reportAssetsDirectory = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
