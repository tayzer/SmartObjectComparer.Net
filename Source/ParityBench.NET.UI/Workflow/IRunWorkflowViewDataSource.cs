using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Workflow;

/// <summary>A saved run profile as the workflow's profile picker sees it.</summary>
public sealed record RunProfileSummary(string Id, string DisplayName);

/// <summary>
/// A run profile resolved for launching: endpoints, comparison settings, optional
/// input directory, and the plugin selection (with its secrets already resolved).
/// </summary>
public sealed record ResolvedRunProfileView(
    Uri EndpointA,
    Uri EndpointB,
    ComparisonOptions Comparison,
    string? RequestDirectory,
    PluginComparisonSelection Selection,
    // The CLR type the plugin's comparison maps both endpoints to, so the workflow
    // UI can browse its properties for ignore/mask rules the same way it does for
    // built-in response models. Null when the plugin/comparison can't be resolved.
    Type? ComparisonType,
    // The plugin comparison's baseline rules (e.g. known-noisy fields it always
    // ignores), mirroring ContractProfileOption.DefaultComparisonRules for the
    // legacy path so the "Profile default ignore rules" preview and the forced
    // toggle state work the same way for plugin profiles. Null alongside
    // ComparisonType when unresolved.
    ComparisonRuleDefaults? PluginDefaultComparisonRules,
    IReadOnlyDictionary<string, string> EndpointAHeaders,
    IReadOnlyDictionary<string, string> EndpointBHeaders);

/// <summary>
/// Supplies create/run/cancel/report actions to shared V2 workflow components without binding them to a host.
/// </summary>
public interface IRunWorkflowViewDataSource
{
    Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists saved run profiles for the profile picker, seeding any that installed
    /// plugins ship as templates. Defaults to none where profiles are unavailable.
    /// </summary>
    Task<IReadOnlyList<RunProfileSummary>> ListRunProfilesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RunProfileSummary>>(Array.Empty<RunProfileSummary>());

    /// <summary>
    /// Resolves a run profile for launching, including resolving its secret references.
    /// </summary>
    Task<ResolvedRunProfileView> ResolveRunProfileAsync(string profileId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("Run profiles are not available in this context.");

    /// <summary>
    /// Lists captured baselines for the baseline picker, newest first. Defaults to
    /// none where the baseline library is unavailable.
    /// </summary>
    Task<IReadOnlyList<BaselineSummary>> ListBaselinesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BaselineSummary>>(Array.Empty<BaselineSummary>());

    /// <summary>
    /// Loads a captured baseline's full provenance for the workflow's baseline panel.
    /// </summary>
    Task<BaselinePackageManifest?> ResolveBaselineAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<BaselinePackageManifest?>(null);

    Type? ResolveResponseModelType(string modelName);

    Task<ComparisonRun> CreateRunFromDirectoryAsync(
        RequestComparisonRunRequest request,
        CancellationToken cancellationToken = default);

    Task<bool> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    Task<ComparisonRun> LoadRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    bool IsRunning(RunId runId);

    Task<StaticReportBundleWriteResult> GenerateReportAsync(
        RunId runId,
        string outputDirectory,
        string? reportAssetsDirectory = null,
        CancellationToken cancellationToken = default);
}
