using AngleSharp.Dom;
using Bunit;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor;
using MudBlazor.Services;

using System.Linq;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Secrets;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Shell;
using ParityBench.NET.UI.Workflow;
using ParityBench.NET.UI.Theming;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ParityBenchHomeTests
{
    private BunitContext testContext = null!;
    private FakeRunWorkflowViewDataSource runWorkflowDataSource = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.Services.AddScoped<ParityBenchThemeState>();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });
        runWorkflowDataSource = new FakeRunWorkflowViewDataSource();
        testContext.Services.AddSingleton<IRunWorkflowViewDataSource>(runWorkflowDataSource);
        testContext.Services.AddSingleton<IRunResultsViewDataSource>(new FakeRunResultsViewDataSource());
        testContext.Services.AddSingleton<IRequestSourcePicker>(new NoOpRequestSourcePicker());
        testContext.Services.AddSingleton<IAcceptedDifferenceUseCases>(new InMemoryAcceptedDifferenceUseCases(isReadOnly: false));

        // With KeepPanelsAlive="true" on ParityBenchHome's MudTabs, all four tab panels
        // render at once instead of only the active one, so Baselines/Plugins & Profiles'
        // dependencies must resolve even though these tests don't exercise them.
        EmptyPluginMetadataProvider pluginMetadata = new EmptyPluginMetadataProvider();
        EmptyRunProfileStore profileStore = new EmptyRunProfileStore();
        testContext.Services.AddSingleton<IBaselineLibraryUseCases>(new EmptyBaselineLibrary());
        testContext.Services.AddSingleton<IPluginMetadataProvider>(pluginMetadata);
        testContext.Services.AddSingleton<IRunProfileStore>(profileStore);
        testContext.Services.AddSingleton<ISecretStore>(new EmptySecretStore());
        testContext.Services.AddSingleton(new PluginProfileBootstrapper(pluginMetadata, profileStore));
    }

    [TestCleanup]
    public async Task TearDown()
    {
        await testContext.DisposeAsync().ConfigureAwait(false);
    }

    [TestMethod]
    public void ParityBenchHome_WhenRendered_ShowsWorkspaceHeaderAndTabs()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();

        StringAssert.Contains(component.Markup, "Request comparison workspace");
        StringAssert.Contains(component.Markup, "Compare Requests");
        StringAssert.Contains(component.Markup, "Run History");
        StringAssert.Contains(component.Markup, "Step 1: Upload Request Files");
        Assert.IsFalse(component.Markup.Contains("Use light mode", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ParityBenchHome_WhenFirstRendered_DoesNotShowHistoryAsPrimarySurface()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();

        // KeepPanelsAlive renders every tab panel's markup up front and hides inactive
        // ones with display:none rather than omitting them, so "No runs found" (Run
        // History) is legitimately present in the full markup — check the active panel
        // (display:contents) specifically for what the user actually sees.
        IElement activePanel = component.Find("div.mud-tab-panel[style='display:contents;']");
        Assert.IsFalse(activePanel.TextContent.Contains("No runs found", StringComparison.Ordinal));
        StringAssert.Contains(component.Markup, "Start Comparison");
    }


    [TestMethod]
    public void ParityBenchHome_WhenRunIsExecuting_DoesNotRenderRequestResultsPanel()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();
        IRenderedComponent<RunWorkflow> workflow = component.FindComponent<RunWorkflow>();

        component.InvokeAsync(() => workflow.Instance.RunChanged.InvokeAsync(CreateExecutingRun(new RunId("run-1")))).GetAwaiter().GetResult();
        Assert.IsFalse(component.Markup.Contains("Request Comparison Results", StringComparison.Ordinal));
    }

    [TestMethod]
    public void ParityBenchHome_WhenRunIsCompleted_RendersRequestResultsPanel()
    {
        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();
        IRenderedComponent<RunWorkflow> workflow = component.FindComponent<RunWorkflow>();

        component.InvokeAsync(() => workflow.Instance.RunChanged.InvokeAsync(CreateCompletedRun(new RunId("run-1")))).GetAwaiter().GetResult();
        component.WaitForAssertion(() => StringAssert.Contains(component.Markup, "Request Comparison Results"));
    }

    [TestMethod]
    public void ParityBenchHome_WhenSwitchingTabsAwayAndBack_PreservesRunProfileSelection()
    {
        runWorkflowDataSource.RunProfilesToReturn = new[]
        {
            new RunProfileSummary("client-customer-lookup-local", "Client Customer Lookup — Local"),
        };
        runWorkflowDataSource.ResolvedProfile = new ResolvedRunProfileView(
            new Uri("https://qa.example.test/soap"),
            new Uri("https://qa.example.test/json"),
            new ComparisonOptions(ignoreXmlNamespaces: true),
            "C:/runs/client",
            new PluginComparisonSelection("client.customer-lookup", "client.customer-lookup.soap-vs-json"),
            null,
            null,
            new Dictionary<string, string>(),
            new Dictionary<string, string>());

        IRenderedComponent<ParityBenchHome> component = testContext.Render<ParityBenchHome>();
        IRenderedComponent<RunWorkflow> workflowBefore = component.FindComponent<RunWorkflow>();

        // The run-profile picker is the first select once profiles are present.
        IRenderedComponent<MudSelect<string>> runProfileSelect = workflowBefore.FindComponent<MudSelect<string>>();
        component.InvokeAsync(() => runProfileSelect.Instance.ValueChanged.InvokeAsync("client-customer-lookup-local"))
            .GetAwaiter().GetResult();

        component.WaitForAssertion(() =>
            StringAssert.Contains(workflowBefore.Markup, "Selected run profile: Client Customer Lookup — Local"));
        string? endpointABeforeSwitch = GetTextFieldValue(workflowBefore, "Endpoint A");
        Assert.AreEqual("https://qa.example.test/soap", endpointABeforeSwitch);

        // Simulate leaving the Compare Requests tab and coming back, the same way
        // MudTabs drives @bind-ActivePanelIndex itself.
        IRenderedComponent<MudTabs> tabs = component.FindComponent<MudTabs>();
        component.InvokeAsync(() => tabs.Instance.ActivePanelIndexChanged.InvokeAsync(3)).GetAwaiter().GetResult();
        component.InvokeAsync(() => tabs.Instance.ActivePanelIndexChanged.InvokeAsync(0)).GetAwaiter().GetResult();

        IRenderedComponent<RunWorkflow> workflowAfter = component.FindComponent<RunWorkflow>();

        // Proves KeepPanelsAlive actually kept the panel resident, not that state
        // happens to match by coincidence.
        Assert.AreSame(workflowBefore.Instance, workflowAfter.Instance);
        StringAssert.Contains(workflowAfter.Markup, "Selected run profile: Client Customer Lookup — Local");
        Assert.AreEqual(endpointABeforeSwitch, GetTextFieldValue(workflowAfter, "Endpoint A"));
    }

    private static string? GetTextFieldValue<TComponent>(IRenderedComponent<TComponent> component, string labelText)
        where TComponent : IComponent
    {
        IElement label = component.FindAll("label")
            .Single(element => string.Equals(element.TextContent.Trim(), labelText, StringComparison.Ordinal));
        string? inputId = label.GetAttribute("for");
        Assert.IsFalse(string.IsNullOrWhiteSpace(inputId));
        return component.Find($"#{inputId}").GetAttribute("value");
    }

    private static ComparisonRun CreateExecutingRun(RunId runId) =>
        ComparisonRun.Create(runId, CreateOptions())
            .Start()
            .Advance(RunStatus.Executing, new RunProgress(25, "Executed 125 of 500 requests.", 125, 500));

    private static ComparisonRun CreateCompletedRun(RunId runId)
    {
        RunResultSummary summary = new RunResultSummary(
            totalPairs: 1,
            equalPairs: 1,
            differentPairs: 0,
            errorPairs: 0,
            detailIndexReference: new RunDetailReference("runs/run-1/details/manifest.json"));

        return ComparisonRun.Create(runId, CreateOptions()).Start().Complete(summary);
    }

    private static RunOptions CreateOptions() =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test"), "Expected"),
            new EndpointDefinition(new Uri("https://service-b.example.test"), "Actual"),
            TimeSpan.FromSeconds(30),
            32);

    private sealed class FakeRunWorkflowViewDataSource : IRunWorkflowViewDataSource
    {
        public IReadOnlyList<RunProfileSummary> RunProfilesToReturn { get; set; } = Array.Empty<RunProfileSummary>();

        public ResolvedRunProfileView? ResolvedProfile { get; set; }

        public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new RequestComparisonDefaults(
                Array.Empty<ResponseModelOption>(),
                Array.Empty<ContractProfileOption>(),
                Array.Empty<EndpointOption>(),
                Array.Empty<RequestComparisonPresetOption>()));

        public Task<IReadOnlyList<RunProfileSummary>> ListRunProfilesAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(RunProfilesToReturn);

        public Task<ResolvedRunProfileView> ResolveRunProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(ResolvedProfile ?? throw new InvalidOperationException("No resolved profile configured."));

        public Type? ResolveResponseModelType(string modelName) => null;

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

    private sealed class EmptyBaselineLibrary : IBaselineLibraryUseCases
    {
        public Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<BaselineSummary>>(Array.Empty<BaselineSummary>());

        public Task<BaselinePackageManifest?> GetAsync(
            BaselineId id,
            int? version = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<BaselinePackageManifest?>(null);

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

    private sealed class EmptyPluginMetadataProvider : IPluginMetadataProvider
    {
        public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginCatalogView(Array.Empty<InstalledPluginMetadata>(), Array.Empty<PluginInstallationFailure>()));

        public Task<PluginCatalogView> RefreshCatalogAsync(CancellationToken cancellationToken = default) =>
            GetCatalogAsync(cancellationToken);

        public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstalledPluginMetadata?>(null);

        public Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<PluginComparisonDefinitionInfo?>(null);
    }

    private sealed class EmptyRunProfileStore : IRunProfileStore
    {
        public Task<IReadOnlyList<RunProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunProfile>>(Array.Empty<RunProfile>());

        public Task<RunProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult<RunProfile?>(null);

        public Task SaveAsync(RunProfile profile, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class EmptySecretStore : ISecretStore
    {
        public bool CanWrite => false;

        public Task<string?> GetAsync(SecretReference reference, CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>(null);

        public Task SetAsync(SecretReference reference, string value, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }
}
