using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Response phase: masks configured paths and persists the raw response as a run
/// artifact, so comparison works against durable artifacts rather than live streams.
/// </summary>
public sealed class ResponsePersistenceMiddleware : IEndpointComparisonMiddleware
{
    private readonly IRunArtifactStore runArtifactStore;
    private readonly RunId runId;
    private readonly IReadOnlyList<MaskRuleDefinition> maskRules;
    private readonly RunExecutionCounters counters;

    public ResponsePersistenceMiddleware(
        IRunArtifactStore runArtifactStore,
        RunId runId,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters)
    {
        ArgumentNullException.ThrowIfNull(runArtifactStore);
        ArgumentNullException.ThrowIfNull(maskRules);
        ArgumentNullException.ThrowIfNull(counters);

        this.runArtifactStore = runArtifactStore;
        this.runId = runId;
        this.maskRules = maskRules;
        this.counters = counters;
    }

    public string StepId => BuiltInStepIds.ResponsePersistence;

    public PipelinePhase Phase => PipelinePhase.Response;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        PipelineTransportResponse response = context.Response
            ?? throw new InvalidOperationException(
                $"No response was captured for endpoint {context.Endpoint} of '{context.Request.RelativePath}'.");

        await using Stream body = await response.Body.OpenReadAsync(cancellationToken).ConfigureAwait(false);

        ResponseArtifactMetadata metadata = await PersistAsync(
            runArtifactStore,
            runId,
            context.Endpoint,
            context.Request,
            response.StatusCode,
            response.ContentType,
            body,
            maskRules,
            counters,
            cancellationToken).ConfigureAwait(false);

        context.ResponseArtifact = metadata;

        // Give later phases (a plugin's mapping step) a way to read the persisted
        // response without exposing the artifact store to plugin code.
        if (context is EndpointPipelineContext concreteContext)
        {
            concreteContext.OpenResponseArtifact = async token =>
                await runArtifactStore.OpenReadAsync(metadata.Artifact, token).ConfigureAwait(false);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Masks and persists a body, updating the run's byte counter. Shared with the
    /// mapping phase, which persists the canonical projection the same way.
    /// </summary>
    internal static async Task<ResponseArtifactMetadata> PersistAsync(
        IRunArtifactStore runArtifactStore,
        RunId runId,
        EndpointSlot endpoint,
        RequestItem request,
        int statusCode,
        string? contentType,
        Stream body,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        await using Stream? maskedBody = await ResponseMasker
            .MaskAsync(body, contentType, maskRules, cancellationToken)
            .ConfigureAwait(false);

        ResponseArtifactMetadata metadata = await runArtifactStore
            .SaveResponseAsync(
                runId,
                endpoint,
                request,
                statusCode,
                contentType,
                maskedBody ?? body,
                cancellationToken)
            .ConfigureAwait(false);

        counters.AddResponseBytes(metadata.ContentLength);
        return metadata;
    }
}
