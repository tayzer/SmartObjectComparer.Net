namespace ParityBench.PluginSdk.Pipeline;

/// <summary>
/// Invokes the remainder of the pipeline. A middleware that does not call this
/// short-circuits every later step in its scope.
/// </summary>
public delegate ValueTask PipelineDelegate(CancellationToken cancellationToken);

/// <summary>
/// Identity and placement shared by every pipeline step. Saved run profiles
/// reference <see cref="StepId"/>, never a CLR type name, so a step can be
/// enabled, disabled and configured without the profile knowing what implements it.
/// </summary>
public interface IComparisonMiddleware
{
    /// <summary>
    /// Gets the stable logical id of this step (e.g. <c>acme.token-exchange</c>).
    /// </summary>
    string StepId { get; }

    /// <summary>
    /// Gets the phase this step runs in.
    /// </summary>
    PipelinePhase Phase { get; }

    /// <summary>
    /// Gets the ordering of this step relative to other steps <em>in the same phase</em>.
    /// Lower runs first; ties fall back to registration order.
    /// </summary>
    int Order { get; }
}

/// <summary>
/// A step in an endpoint-scoped phase (<see cref="PipelinePhase.Input"/> through
/// <see cref="PipelinePhase.Mapping"/>). Runs once for endpoint A and once for
/// endpoint B, concurrently, so implementations must not share mutable state.
/// </summary>
public interface IEndpointComparisonMiddleware : IComparisonMiddleware
{
    ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken);
}

/// <summary>
/// A step in a pair-scoped phase (<see cref="PipelinePhase.Comparison"/>,
/// <see cref="PipelinePhase.ResultProcessing"/>). Runs once per request pair,
/// after both endpoint slots produced their comparison instance.
/// </summary>
public interface IPairComparisonMiddleware : IComparisonMiddleware
{
    ValueTask InvokeAsync(
        IPairPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken);
}
