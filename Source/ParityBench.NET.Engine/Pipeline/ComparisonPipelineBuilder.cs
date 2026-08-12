using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline;

/// <summary>
/// Assembles a <see cref="ComparisonPipeline"/> from registered steps. Steps are
/// bucketed by phase and the buckets are concatenated in fixed phase order, so a
/// plugin can only influence ordering <em>within</em> a phase — an invalid pipeline
/// (mapping before transport, comparison before mapping) cannot be expressed.
/// </summary>
public sealed class ComparisonPipelineBuilder
{
    private readonly List<Registration> registrations = new List<Registration>();
    private readonly HashSet<string> stepIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public ComparisonPipelineBuilder Add(IComparisonMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);

        if (string.IsNullOrWhiteSpace(middleware.StepId))
        {
            throw new ArgumentException("Pipeline step id must not be empty.", nameof(middleware));
        }

        bool isEndpointStep = middleware is IEndpointComparisonMiddleware;
        bool isPairStep = middleware is IPairComparisonMiddleware;

        if (isEndpointStep == isPairStep)
        {
            throw new InvalidOperationException(
                $"Pipeline step '{middleware.StepId}' must implement exactly one of {nameof(IEndpointComparisonMiddleware)} or {nameof(IPairComparisonMiddleware)}.");
        }

        PipelineScope declaredScope = middleware.Phase.GetScope();
        PipelineScope implementedScope = isEndpointStep ? PipelineScope.Endpoint : PipelineScope.Pair;
        if (declaredScope != implementedScope)
        {
            throw new InvalidOperationException(
                $"Pipeline step '{middleware.StepId}' declares phase '{middleware.Phase}' ({declaredScope} scope) but is a {implementedScope}-scoped middleware.");
        }

        if (!stepIds.Add(middleware.StepId))
        {
            throw new InvalidOperationException($"Pipeline step '{middleware.StepId}' is already registered.");
        }

        registrations.Add(new Registration(middleware, registrations.Count));
        return this;
    }

    public ComparisonPipelineBuilder AddRange(IEnumerable<IComparisonMiddleware> middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);

        foreach (IComparisonMiddleware step in middleware)
        {
            Add(step);
        }

        return this;
    }

    public ComparisonPipeline Build(DetailedCompareMetricsCollector? timing = null)
    {
        Registration[] ordered = registrations
            .OrderBy(registration => (int)registration.Middleware.Phase)
            .ThenBy(registration => registration.Middleware.Order)
            .ThenBy(registration => registration.Sequence)
            .ToArray();

        return new ComparisonPipeline(
            ordered.Select(registration => registration.Middleware).OfType<IEndpointComparisonMiddleware>().ToArray(),
            ordered.Select(registration => registration.Middleware).OfType<IPairComparisonMiddleware>().ToArray(),
            timing);
    }

    // Registration order is the final tie-break so steps that share a phase and an
    // order stay in the sequence the caller registered them in.
    private readonly record struct Registration(IComparisonMiddleware Middleware, int Sequence);
}
