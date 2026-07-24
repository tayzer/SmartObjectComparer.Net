using ParityBench.NET.Domain.Requests;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Request phase: merges endpoint-level, request-level and slot-specific headers
/// into the outbound header set. Runs first in its phase so plugin auth steps can
/// overwrite or add to the merged result.
/// </summary>
public sealed class HeaderMergeMiddleware : IEndpointComparisonMiddleware
{
    public string StepId => BuiltInStepIds.HeaderMerge;

    public PipelinePhase Phase => PipelinePhase.Request;

    // Negative so it stays ahead of plugin steps that default to Order 0.
    public int Order => -1000;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        // Later sources win: endpoint defaults, then request-wide headers, then the
        // headers a request declares for this specific slot.
        Apply(context.RequestHeaders, context.EndpointDefinition.Headers);
        Apply(context.RequestHeaders, context.Request.Headers);
        Apply(context.RequestHeaders, context.Request.GetHeaders(context.Endpoint));

        await next(cancellationToken).ConfigureAwait(false);
    }

    private static void Apply(IDictionary<string, string> target, IReadOnlyDictionary<string, string>? source)
    {
        if (source is null)
        {
            return;
        }

        foreach (KeyValuePair<string, string> header in source)
        {
            target[header.Key] = header.Value;
        }
    }
}
