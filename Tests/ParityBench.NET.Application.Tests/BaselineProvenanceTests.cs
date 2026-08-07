using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Tests;

/// <summary>
/// What a report says about a baseline run: which package produced the expected side,
/// and whether anything about the two sides' provenance should be treated as suspect.
/// </summary>
[TestClass]
public sealed class BaselineProvenanceTests
{
    [TestMethod]
    public async Task CreateAsync_ForALiveVsLiveRun_ProducesNoProvenance()
    {
        BaselineReportProvenance? provenance = await BaselineProvenanceFactory.CreateAsync(
            new FakeBaselineStore(),
            CreateRun(baseline: null));

        Assert.IsNull(provenance);
    }

    [TestMethod]
    public async Task CreateAsync_ForAReplay_DescribesBothSides()
    {
        BaselineReportProvenance? provenance = await BaselineProvenanceFactory.CreateAsync(
            new FakeBaselineStore(CreateManifest()),
            CreateRun(BaselineBinding.ForReplay(new BaselineId("orders"), 3)));

        Assert.IsNotNull(provenance);
        Assert.AreEqual("Baseline vs Live", provenance.ModeLabel);
        Assert.AreEqual("Orders upgrade", provenance.DisplayName);
        Assert.AreEqual("v3", provenance.DisplayVersion);
        Assert.AreEqual("2.1.0", provenance.CapturePluginVersion);
        Assert.AreEqual("2.1.0", provenance.LivePluginVersion);
        Assert.AreEqual(1, provenance.ScenarioCount);
        Assert.IsFalse(provenance.HasProvenanceWarning);
    }

    [TestMethod]
    public async Task CreateAsync_WhenThePluginVersionChangedSinceCapture_WarnsAboutTheMapping()
    {
        BaselineReportProvenance? provenance = await BaselineProvenanceFactory.CreateAsync(
            new FakeBaselineStore(CreateManifest(pluginVersion: "1.0.0")),
            CreateRun(BaselineBinding.ForReplay(new BaselineId("orders"), 3)));

        Assert.IsTrue(provenance!.PluginVersionChanged);
        Assert.IsTrue(provenance.HasProvenanceWarning);
    }

    [TestMethod]
    public async Task CreateAsync_WhenTheEnvironmentChangedSinceCapture_WarnsAboutTheEnvironment()
    {
        BaselineReportProvenance? provenance = await BaselineProvenanceFactory.CreateAsync(
            new FakeBaselineStore(CreateManifest(environmentName: "production")),
            CreateRun(BaselineBinding.ForReplay(new BaselineId("orders"), 3)));

        Assert.IsTrue(provenance!.EnvironmentChanged);
        Assert.IsFalse(provenance.PluginVersionChanged);
    }

    [TestMethod]
    public async Task CreateAsync_ForACapture_FindsThePackageThatRunWrote()
    {
        // A capture run does not know its version up front, so the package is found by
        // the run that produced it.
        FakeBaselineStore store = new FakeBaselineStore(CreateManifest(capturedFromRunId: "run-1"));

        BaselineReportProvenance? provenance = await BaselineProvenanceFactory.CreateAsync(
            store,
            CreateRun(BaselineBinding.ForCapture("Orders upgrade"), runId: "run-1"));

        Assert.AreEqual(BaselineRunMode.CaptureBaseline, provenance!.Mode);
        Assert.AreEqual("Baseline Capture", provenance.ModeLabel);
        Assert.AreEqual(3, provenance.BaselineVersion);
    }

    [TestMethod]
    public void FromRun_WhenTheRunReplayedABaseline_TitlesTheReportAfterTheMode()
    {
        StaticReportMetadata metadata = StaticReportMetadata.FromRun(
            CreateRun(BaselineBinding.ForReplay(new BaselineId("orders"), 3)),
            DateTimeOffset.UtcNow,
            new BaselineReportProvenance(BaselineRunMode.BaselineVsLive, "orders", "Orders upgrade", 3));

        Assert.AreEqual("Baseline vs Live Report", metadata.ReportTitle);
        Assert.IsNotNull(metadata.Baseline);
    }

    [TestMethod]
    public void FromRun_WhenTheRunWasLiveVsLive_KeepsTheOriginalTitleAndNoProvenance()
    {
        StaticReportMetadata metadata = StaticReportMetadata.FromRun(CreateRun(baseline: null), DateTimeOffset.UtcNow);

        Assert.AreEqual("Comparison Report", metadata.ReportTitle);
        Assert.IsNull(metadata.Baseline);
    }

    [TestMethod]
    public void StaticReportManifest_StillLoadsBundlesWrittenBeforeBaselinesExisted()
    {
        Assert.AreEqual(3, StaticReportManifest.CurrentSchemaVersion);

        StaticReportManifest manifest = new StaticReportManifest(
            2,
            DateTimeOffset.UtcNow,
            StaticReportRunSnapshot.FromRun(CreateRun(baseline: null)),
            null,
            StaticReportManifest.DefaultDetailPageSize);

        Assert.AreEqual(2, manifest.SchemaVersion);
        Assert.IsNull(manifest.Metadata?.Baseline);
    }

    private static ComparisonRun CreateRun(BaselineBinding? baseline, string runId = "run-1") =>
        ComparisonRun.Create(
            new RunId(runId),
            new RunOptions(
                new RequestBatchReference("batch-1"),
                new EndpointDefinition(new Uri("https://a.example.test")),
                new EndpointDefinition(new Uri("https://b.example.test")),
                TimeSpan.FromSeconds(30),
                2,
                pluginComparison: new PluginComparisonSelection(
                    "client.lookup",
                    "client.lookup.customer",
                    "2.1.0",
                    environmentName: "staging"),
                baseline: baseline));

    private static BaselinePackageManifest CreateManifest(
        string? pluginVersion = "2.1.0",
        string? environmentName = "staging",
        string capturedFromRunId = "run-0") =>
        new BaselinePackageManifest(
            new BaselineId("orders"),
            "Orders upgrade",
            3,
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            capturedFromRunId,
            new Uri("https://legacy.example.test/lookup"),
            "client.lookup",
            "client.lookup.customer",
            pluginVersion,
            environmentName,
            scenarios: new[]
            {
                new BaselineScenarioEntry("one.xml", "application/xml", 12, 200, "text/xml", "hash", 24),
            });

    private sealed class FakeBaselineStore : IBaselineStore
    {
        private readonly BaselinePackageManifest? manifest;

        public FakeBaselineStore(BaselinePackageManifest? manifest = null)
        {
            this.manifest = manifest;
        }

        public Task<BaselinePackageManifest?> LoadManifestAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(manifest);

        public Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaselineSummary>>(manifest is null
                ? Array.Empty<BaselineSummary>()
                : new[] { BaselineSummary.FromManifest(manifest, 1024) });

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

        public Task<int> ExportRequestsToDirectoryAsync(
            BaselineId id,
            int version,
            string targetDirectory,
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
}
