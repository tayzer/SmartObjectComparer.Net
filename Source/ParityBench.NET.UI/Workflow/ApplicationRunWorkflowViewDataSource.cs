using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Workflow;

public sealed class ApplicationRunWorkflowViewDataSource : IRunWorkflowViewDataSource
{
    private readonly IRequestComparisonWorkflowUseCases workflowUseCases;
    private readonly IComparisonRunJobUseCases jobUseCases;
    private readonly IComparisonRunResultUseCases resultUseCases;
    private readonly IRequestComparisonDefaultsUseCases defaultsUseCases;
    private readonly IResponseModelRegistry responseModelRegistry;

    public ApplicationRunWorkflowViewDataSource(
        IRequestComparisonWorkflowUseCases workflowUseCases,
        IComparisonRunJobUseCases jobUseCases,
        IComparisonRunResultUseCases resultUseCases,
        IRequestComparisonDefaultsUseCases defaultsUseCases,
        IResponseModelRegistry responseModelRegistry)
    {
        this.workflowUseCases = workflowUseCases ?? throw new ArgumentNullException(nameof(workflowUseCases));
        this.jobUseCases = jobUseCases ?? throw new ArgumentNullException(nameof(jobUseCases));
        this.resultUseCases = resultUseCases ?? throw new ArgumentNullException(nameof(resultUseCases));
        this.defaultsUseCases = defaultsUseCases ?? throw new ArgumentNullException(nameof(defaultsUseCases));
        this.responseModelRegistry = responseModelRegistry ?? throw new ArgumentNullException(nameof(responseModelRegistry));
    }

    public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default) =>
        defaultsUseCases.LoadDefaultsAsync(cancellationToken);

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