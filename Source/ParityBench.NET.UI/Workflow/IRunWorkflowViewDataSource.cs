using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
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
    PluginComparisonSelection Selection);

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