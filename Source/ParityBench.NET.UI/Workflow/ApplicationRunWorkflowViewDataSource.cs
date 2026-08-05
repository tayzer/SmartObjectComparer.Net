using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Workflow;

public sealed class ApplicationRunWorkflowViewDataSource : IRunWorkflowViewDataSource
{
    private readonly IRequestComparisonWorkflowUseCases workflowUseCases;
    private readonly IComparisonRunJobUseCases jobUseCases;
    private readonly IComparisonRunResultUseCases resultUseCases;
    private readonly IRequestComparisonDefaultsUseCases defaultsUseCases;
    private readonly IResponseModelRegistry responseModelRegistry;
    private readonly IRunProfileStore runProfileStore;
    private readonly RunProfileResolver runProfileResolver;
    private readonly PluginProfileBootstrapper profileBootstrapper;
    private readonly IPluginMetadataProvider pluginMetadataProvider;
    private readonly IBaselineLibraryUseCases? baselineLibrary;

    public ApplicationRunWorkflowViewDataSource(
        IRequestComparisonWorkflowUseCases workflowUseCases,
        IComparisonRunJobUseCases jobUseCases,
        IComparisonRunResultUseCases resultUseCases,
        IRequestComparisonDefaultsUseCases defaultsUseCases,
        IResponseModelRegistry responseModelRegistry,
        IRunProfileStore runProfileStore,
        RunProfileResolver runProfileResolver,
        PluginProfileBootstrapper profileBootstrapper,
        IPluginMetadataProvider pluginMetadataProvider,
        IBaselineLibraryUseCases? baselineLibrary = null)
    {
        this.baselineLibrary = baselineLibrary;
        this.workflowUseCases = workflowUseCases ?? throw new ArgumentNullException(nameof(workflowUseCases));
        this.jobUseCases = jobUseCases ?? throw new ArgumentNullException(nameof(jobUseCases));
        this.resultUseCases = resultUseCases ?? throw new ArgumentNullException(nameof(resultUseCases));
        this.defaultsUseCases = defaultsUseCases ?? throw new ArgumentNullException(nameof(defaultsUseCases));
        this.responseModelRegistry = responseModelRegistry ?? throw new ArgumentNullException(nameof(responseModelRegistry));
        this.runProfileStore = runProfileStore ?? throw new ArgumentNullException(nameof(runProfileStore));
        this.runProfileResolver = runProfileResolver ?? throw new ArgumentNullException(nameof(runProfileResolver));
        this.profileBootstrapper = profileBootstrapper ?? throw new ArgumentNullException(nameof(profileBootstrapper));
        this.pluginMetadataProvider = pluginMetadataProvider ?? throw new ArgumentNullException(nameof(pluginMetadataProvider));
    }

    public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
        defaultsUseCases.LoadDefaultsAsync(cancellationToken);

    public async Task<IReadOnlyList<RunProfileSummary>> ListRunProfilesAsync(CancellationToken cancellationToken = default)
    {
        // Seed profiles from installed plugin templates, then list, so a freshly
        // installed plugin's profile appears in the picker with no manual step.
        await profileBootstrapper.EnsureTemplateProfilesAsync(cancellationToken).ConfigureAwait(false);
        IReadOnlyList<RunProfile> profiles = await runProfileStore.ListAsync(cancellationToken).ConfigureAwait(false);
        return profiles.Select(profile => new RunProfileSummary(profile.Id, profile.DisplayName)).ToArray();
    }

    public async Task<ResolvedRunProfileView> ResolveRunProfileAsync(string profileId, CancellationToken cancellationToken = default)
    {
        ResolvedRunProfile resolved = await runProfileResolver.ResolveAsync(profileId, cancellationToken).ConfigureAwait(false);
        PluginComparisonDefinitionInfo? comparisonDefinition = await pluginMetadataProvider.ResolveComparisonDefinitionAsync(
            resolved.Selection.PluginId,
            resolved.Selection.ComparisonId,
            resolved.Selection.PluginVersion,
            cancellationToken).ConfigureAwait(false);

        return new ResolvedRunProfileView(
            resolved.EndpointA.Uri,
            resolved.EndpointB.Uri,
            resolved.Profile.Comparison,
            resolved.Profile.RequestDirectory,
            resolved.Selection,
            comparisonDefinition?.ComparisonType,
            comparisonDefinition?.DefaultComparisonRules,
            resolved.EndpointA.Headers,
            resolved.EndpointB.Headers,
            resolved.Profile.RetentionModeOverride);
    }

    public async Task<IReadOnlyList<BaselineSummary>> ListBaselinesAsync(CancellationToken cancellationToken = default) =>
        baselineLibrary is null
            ? Array.Empty<BaselineSummary>()
            : await baselineLibrary.ListAsync(cancellationToken).ConfigureAwait(false);

    public async Task<BaselinePackageManifest?> ResolveBaselineAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default) =>
        baselineLibrary is null
            ? null
            : await baselineLibrary.GetAsync(id, version, cancellationToken).ConfigureAwait(false);

    public Type? ResolveResponseModelType(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName) || string.Equals(modelName, "Auto", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return responseModelRegistry.Resolve(modelName);
    }

    public Task<ComparisonRun> CreateRunFromDirectoryAsync(
        RequestComparisonRunRequest request,
        CancellationToken cancellationToken = default) =>
        workflowUseCases.CreateRunFromDirectoryAsync(request, cancellationToken);

    public Task<bool> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        jobUseCases.StartRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        jobUseCases.CancelRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> LoadRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunAsync(runId, cancellationToken);

    public bool IsRunning(RunId runId) => jobUseCases.IsRunning(runId);

    public Task<StaticReportBundleWriteResult> GenerateReportAsync(
        RunId runId,
        string outputDirectory,
        string? reportAssetsDirectory = null,
        CancellationToken cancellationToken = default) =>
        workflowUseCases.GenerateReportAsync(runId, outputDirectory, reportAssetsDirectory, cancellationToken);
}
