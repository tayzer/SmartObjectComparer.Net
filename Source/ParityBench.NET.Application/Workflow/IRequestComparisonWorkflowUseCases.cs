using ParityBench.NET.Application.Reports;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Workflow;

/// <summary>
/// Coordinates host-facing request comparison workflows.
/// </summary>
public interface IRequestComparisonWorkflowUseCases
{
    /// <summary>
    /// Stages a request directory and creates a comparison run.
    /// </summary>
    Task<ComparisonRun> CreateRunFromDirectoryAsync(
        RequestComparisonRunRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts a created run and waits for it to reach a terminal state.
    /// </summary>
    Task<ComparisonRun> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a non-terminal run.
    /// </summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>Cancels a non-terminal run with a supplied lifecycle message.</summary>
    Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default) =>
        CancelRunAsync(runId, cancellationToken);

    /// <summary>
    /// Generates a static bundled report for a run.
    /// </summary>
    Task<StaticReportBundleWriteResult> GenerateReportAsync(
        RunId runId,
        string outputDirectory,
        string? reportAssetsDirectory = null,
        CancellationToken cancellationToken = default);
}
