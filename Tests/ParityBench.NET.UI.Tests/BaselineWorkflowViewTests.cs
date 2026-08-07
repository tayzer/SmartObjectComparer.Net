using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Baselines;
using ParityBench.NET.UI.Theming;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.UI.Tests;

/// <summary>
/// The baseline surfaces of the workflow: choosing a comparison mode, and the library
/// panel that lists captured packages.
/// </summary>
[TestClass]
public sealed class BaselineWorkflowViewTests
{
    private BunitContext testContext = null!;
    private FakeBaselineLibrary library = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.Services.AddScoped<ParityBenchThemeState>();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
        library = new FakeBaselineLibrary();
        testContext.Services.AddSingleton<IBaselineLibraryUseCases>(library);
        testContext.Services.AddSingleton<IRunWorkflowViewDataSource>(new BaselineAwareWorkflowDataSource(library));
        testContext.Services.AddSingleton<IRequestSourcePicker>(new NoOpRequestSourcePicker());
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void RunWorkflow_WhenRendered_OffersTheComparisonModes()
    {
        IRenderedComponent<RunWorkflow> component = testContext.Render<RunWorkflow>();

        StringAssert.Contains(component.Markup, "Comparison Mode");
        // The default stays live-vs-live, so an existing workflow is unchanged.
        StringAssert.Contains(component.Markup, "Live vs Live");
        StringAssert.Contains(component.Markup, "Step 1: Upload Request Files");
    }

    [TestMethod]
    public void BaselineLibraryPanel_WhenPackagesExist_ListsThemWithProvenance()
    {
        IRenderedComponent<BaselineLibraryPanel> component = testContext.Render<BaselineLibraryPanel>();

        component.WaitForAssertion(() =>
        {
            StringAssert.Contains(component.Markup, "Orders upgrade v2");
            StringAssert.Contains(component.Markup, "12 scenarios");
        });
    }

    [TestMethod]
    public void BaselineLibraryPanel_WhenNoPackagesExist_ExplainsHowToCaptureOne()
    {
        library.Baselines = Array.Empty<BaselineSummary>();

        IRenderedComponent<BaselineLibraryPanel> component = testContext.Render<BaselineLibraryPanel>();

        component.WaitForAssertion(() =>
            StringAssert.Contains(component.Markup, "No baselines captured yet"));
    }

    private sealed class FakeBaselineLibrary : IBaselineLibraryUseCases
    {
        public IReadOnlyList<BaselineSummary> Baselines { get; set; } = new[]
        {
            new BaselineSummary(
                new BaselineId("orders"),
                "Orders upgrade",
                2,
                new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
                new Uri("https://legacy.example.test/lookup"),
                "client.lookup",
                "client.lookup.customer",
                "2.1.0",
                "staging",
                12,
                4096),
        };

        public Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Baselines);

        public Task<BaselinePackageManifest?> GetAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaselinePackageManifest?>(new BaselinePackageManifest(
                id,
                "Orders upgrade",
                version ?? 2,
                new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
                "run-0",
                new Uri("https://legacy.example.test/lookup"),
                "client.lookup",
                "client.lookup.customer"));

        public Task ExportAsync(
            BaselineId id,
            int version,
            string archivePath,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<BaselinePackageManifest> ImportAsync(
            string archivePath,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class BaselineAwareWorkflowDataSource : IRunWorkflowViewDataSource
    {
        private readonly FakeBaselineLibrary library;

        public BaselineAwareWorkflowDataSource(FakeBaselineLibrary library)
        {
            this.library = library;
        }

        public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestComparisonDefaults(
                Array.Empty<ResponseModelOption>(),
                Array.Empty<ContractProfileOption>(),
                Array.Empty<EndpointOption>(),
                Array.Empty<RequestComparisonPresetOption>(),
                new RequestComparisonRunDefaults()));

        public Task<IReadOnlyList<BaselineSummary>> ListBaselinesAsync(CancellationToken cancellationToken = default) =>
            library.ListAsync(cancellationToken);

        public Task<BaselinePackageManifest?> ResolveBaselineAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            library.GetAsync(id, version, cancellationToken);

        public Type? ResolveResponseModelType(string modelName) => null;

        public Task<ComparisonRun> CreateRunFromDirectoryAsync(
            RequestComparisonRunRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> StartRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> CancelRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public bool IsRunning(RunId runId) => false;

        public Task<StaticReportBundleWriteResult> GenerateReportAsync(
            RunId runId,
            string outputDirectory,
            string? reportAssetsDirectory = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class NoOpRequestSourcePicker : IRequestSourcePicker
    {
        public bool IsAvailable => false;

        public Task<string?> PickRequestDirectoryAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<string>> PickRequestFilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());
    }
}
