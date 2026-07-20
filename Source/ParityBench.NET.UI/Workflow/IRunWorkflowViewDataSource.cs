using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Workflow;

/// <summary>
/// Supplies create/run/cancel/report actions to shared V2 workflow components without binding them to a host.
/// </summary>
public interface IRunWorkflowViewDataSource
{
    Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default);

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