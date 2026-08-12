using System.Diagnostics;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Pipeline.BuiltIn;

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
    private readonly DetailedCompareMetricsCollector? timing;

    internal ComparisonPipeline(
        IReadOnlyList<IEndpointComparisonMiddleware> endpointSteps,
        IReadOnlyList<IPairComparisonMiddleware> pairSteps,
        DetailedCompareMetricsCollector? timing = null)
    {
        this.endpointSteps = endpointSteps;
        this.pairSteps = pairSteps;
        this.timing = timing;
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
    private ValueTask InvokeAsync<TStep>(
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
                return InvokeTimedAsync(step, context, tail, invoke, token);
            };
        }

        return next(cancellationToken);
    }

    private async ValueTask InvokeTimedAsync<TStep>(
        TStep step,
        IPipelineContext context,
        PipelineDelegate tail,
        Func<TStep, IPipelineContext, PipelineDelegate, CancellationToken, ValueTask> invoke,
        CancellationToken cancellationToken)
        where TStep : IComparisonMiddleware
    {
        Action<DetailedCompareMetricsCollector, TimeSpan>? record = GetPluginTimingRecorder(step);
        if (record is null)
        {
            await invoke(step, context, tail, cancellationToken).ConfigureAwait(false);
            return;
        }

        Stopwatch stopwatch = Stopwatch.StartNew();
        PipelineDelegate timedTail = async token =>
        {
            stopwatch.Stop();
            try { await tail(token).ConfigureAwait(false); }
            finally { stopwatch.Start(); }
        };

        await invoke(step, context, timedTail, cancellationToken).ConfigureAwait(false);
        stopwatch.Stop();
        record(timing!, stopwatch.Elapsed);
    }

    private Action<DetailedCompareMetricsCollector, TimeSpan>? GetPluginTimingRecorder(IComparisonMiddleware step)
    {
        if (timing is null
            || string.Equals(step.StepId, BuiltInStepIds.CanonicalMapping, StringComparison.Ordinal)
            || string.Equals(step.StepId, BuiltInStepIds.CompareNetObjects, StringComparison.Ordinal))
        {
            return null;
        }

        return step.Phase == PipelinePhase.Mapping
            ? static (collector, elapsed) => collector.AddPluginMapping(elapsed)
            : step.Phase == PipelinePhase.Comparison
                ? static (collector, elapsed) => collector.AddPluginPairProcessing(elapsed)
                : null;
    }
}
