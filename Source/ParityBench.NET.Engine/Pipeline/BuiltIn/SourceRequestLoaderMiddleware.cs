using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Requests;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Input phase: makes the source request body available as the outbound body.
/// Plugin request-phase steps replace it when the two endpoints do not take the
/// same payload; when they do, this is the whole request pipeline.
/// </summary>
public sealed class SourceRequestLoaderMiddleware : IEndpointComparisonMiddleware
{
    private readonly IComparisonDefinition? definition;
    private readonly string? contentTypeOverride;

    public SourceRequestLoaderMiddleware(
        IComparisonDefinition? definition = null,
        string? contentTypeOverride = null)
    {
        this.definition = definition;
        this.contentTypeOverride = string.IsNullOrWhiteSpace(contentTypeOverride) ? null : contentTypeOverride.Trim();
    }

    public string StepId => BuiltInStepIds.SourceRequestLoader;

    public PipelinePhase Phase => PipelinePhase.Input;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        if (context.RequestBody is null)
        {
            // The stream is opened lazily by the transport phase so a request body
            // is never held open while earlier steps run.
            context.RequestBody = new ContractPayload(
                context.SourceFormat,
                ResolveContentType(context),
                context.OpenSourceRequestBodyAsync,
                contentLength: context.Request.ContentLength > 0 ? context.Request.ContentLength : null);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The operator's run-level override wins, then the content type the comparison
    /// declares for this slot, and only then the source request's own — which is
    /// inferred from the request file's extension, so it is a guess, not a statement.
    /// A plugin request step still overrides all three by replacing the body.
    /// </summary>
    private string ResolveContentType(IEndpointPipelineContext context) =>
        contentTypeOverride
            ?? EndpointProfileFor(context.Endpoint)?.RequestContentType
            ?? context.SourceContentType;

    private ContractEndpointProfile? EndpointProfileFor(EndpointSlot endpoint) =>
        endpoint == EndpointSlot.A ? definition?.EndpointA : definition?.EndpointB;
}
