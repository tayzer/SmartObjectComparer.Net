using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline;

/// <summary>
/// A built pipeline: two chains of responsibility, one per scope. Endpoint steps
/// run once per endpoint slot; pair steps run once both slots produced their
/// comparison instance.
/// </summary>
public sealed class ComparisonPipeline
{
    private readonly IReadOnlyList<IEndpointComparisonMiddleware> endpointSteps;
    private readonly IReadOnlyList<IPairComparisonMiddleware> pairSteps;

    internal ComparisonPipeline(
        IReadOnlyList<IEndpointComparisonMiddleware> endpointSteps,
        IReadOnlyList<IPairComparisonMiddleware> pairSteps)
    {
        this.endpointSteps = endpointSteps;
        this.pairSteps = pairSteps;
    }

    public IReadOnlyList<IComparisonMiddleware> EndpointSteps => (IReadOnlyList<IComparisonMiddleware>)endpointSteps;

    public IReadOnlyList<IComparisonMiddleware> PairSteps => (IReadOnlyList<IComparisonMiddleware>)pairSteps;

    public ValueTask ExecuteEndpointAsync(IEndpointPipelineContext context, CancellationToken cancellationToken = default) =>
        ExecuteEndpointAsync(context, PipelinePhase.Input, PipelinePhase.Mapping, cancellationToken);

    /// <summary>
    /// Runs the endpoint steps whose phase falls in the given inclusive range.
    /// </summary>
    /// <remarks>
    /// The executor splits the endpoint chain across its two worker pools: Input
    /// through Response run in the network-bound execute pool while the response
    /// stream is open, and Mapping runs later in the CPU-bound compare pool against
    /// the persisted artifact. Materialized comparison objects therefore never
    /// queue up between the pools, which is what keeps a large run's memory bounded
    /// by concurrency instead of by run size.
    /// </remarks>
    public ValueTask ExecuteEndpointAsync(
        IEndpointPipelineContext context,
        PipelinePhase fromPhase,
        PipelinePhase toPhase,
        CancellationToken cancellationToken = default)
    {
        IEndpointComparisonMiddleware[] steps = endpointSteps
            .Where(step => step.Phase >= fromPhase && step.Phase <= toPhase)
            .ToArray();

        return InvokeAsync(steps, context, static (step, ctx, next, token) => step.InvokeAsync((IEndpointPipelineContext)ctx, next, token), cancellationToken);
    }

    public ValueTask ExecutePairAsync(IPairPipelineContext context, CancellationToken cancellationToken = default) =>
        InvokeAsync(pairSteps, context, static (step, ctx, next, token) => step.InvokeAsync((IPairPipelineContext)ctx, next, token), cancellationToken);

    // The chain is built back to front so each step closes over the tail after it.
    // Every link re-checks IsFailed, so a step that fails without short-circuiting
    // still stops the pipeline instead of letting later steps run on broken state.
    private static ValueTask InvokeAsync<TStep>(
        IReadOnlyList<TStep> steps,
        IPipelineContext context,
        Func<TStep, IPipelineContext, PipelineDelegate, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken)
        where TStep : IComparisonMiddleware
    {
        PipelineDelegate next = static _ => ValueTask.CompletedTask;

        for (int index = steps.Count - 1; index >= 0; index--)
        {
            TStep step = steps[index];
            PipelineDelegate tail = next;
            next = token =>
            {
                if (context.IsFailed)
                {
                    return ValueTask.CompletedTask;
                }

                token.ThrowIfCancellationRequested();
                return invoke(step, context, tail, token);
            };
        }

        return next(cancellationToken);
    }
}
