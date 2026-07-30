using ParityBench.NET.Application.ContractProfiles;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Input phase: makes the source request body available as the outbound body.
/// Plugin request-phase steps replace it when the two endpoints do not take the
/// same payload; when they do, this is the whole request pipeline.
/// </summary>
public sealed class SourceRequestLoaderMiddleware : IEndpointComparisonMiddleware
{
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
                context.SourceContentType,
                context.OpenSourceRequestBodyAsync,
                contentLength: context.Request.ContentLength > 0 ? context.Request.ContentLength : null);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }
}
