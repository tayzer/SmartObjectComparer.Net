using System.Collections.Concurrent;

using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Workflow;

public sealed class ComparisonRunJobService : IComparisonRunJobUseCases
{
    private readonly IRequestComparisonWorkflowUseCases workflowUseCases;
    private readonly ConcurrentDictionary<RunId, byte> runningRuns = new ConcurrentDictionary<RunId, byte>();

    public ComparisonRunJobService(IRequestComparisonWorkflowUseCases workflowUseCases)
    {
        this.workflowUseCases = workflowUseCases ?? throw new ArgumentNullException(nameof(workflowUseCases));
    }

    public Task<bool> StartRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        if (!runningRuns.TryAdd(runId, 0))
        {
            return Task.FromResult(false);
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await workflowUseCases.StartRunAsync(runId, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                runningRuns.TryRemove(runId, out _);
            }
        }, CancellationToken.None);

        return Task.FromResult(true);
    }

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default) =>
        workflowUseCases.CancelRunAsync(runId, cancellationToken);

    public Task<ComparisonRun> CancelRunAsync(
        RunId runId,
        string? cancellationMessage,
        CancellationToken cancellationToken = default) =>
        workflowUseCases.CancelRunAsync(runId, cancellationMessage, cancellationToken);

    public bool IsRunning(RunId runId) => runningRuns.ContainsKey(runId);
}
