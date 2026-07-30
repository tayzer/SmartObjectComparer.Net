using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline.BuiltIn;

/// <summary>
/// Mapping phase, run last so plugin mapping steps get first refusal.
/// </summary>
/// <remarks>
/// Two jobs, depending on what earlier steps left behind:
/// <list type="bullet">
/// <item>No comparison instance yet — both endpoints speak the comparison type
/// directly, so the persisted response is deserialized into it.</item>
/// <item>A plugin produced the comparison instance — it is serialized back out and
/// persisted as the canonical artifact, and the context's artifact is repointed at
/// it so the report shows what was actually compared, not the pre-mapping payload.</item>
/// </list>
/// </remarks>
public sealed class CanonicalMappingMiddleware : IEndpointComparisonMiddleware
{
    private readonly IComparisonDefinition definition;
    private readonly IContractPayloadSerializer serializer;
    private readonly IRunArtifactStore runArtifactStore;
    private readonly RunId runId;
    private readonly IReadOnlyList<MaskRuleDefinition> maskRules;
    private readonly RunExecutionCounters counters;

    public CanonicalMappingMiddleware(
        IComparisonDefinition definition,
        IContractPayloadSerializer serializer,
        IRunArtifactStore runArtifactStore,
        RunId runId,
        IReadOnlyList<MaskRuleDefinition> maskRules,
        RunExecutionCounters counters)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(runArtifactStore);
        ArgumentNullException.ThrowIfNull(maskRules);
        ArgumentNullException.ThrowIfNull(counters);

        this.definition = definition;
        this.serializer = serializer;
        this.runArtifactStore = runArtifactStore;
        this.runId = runId;
        this.maskRules = maskRules;
        this.counters = counters;
    }

    public string StepId => BuiltInStepIds.CanonicalMapping;

    public PipelinePhase Phase => PipelinePhase.Mapping;

    // Positive so plugin mapping steps (Order 0 by convention) run first.
    public int Order => 1000;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        ResponseArtifactMetadata artifact = context.ResponseArtifact
            ?? throw new InvalidOperationException(
                $"No response artifact was persisted for endpoint {context.Endpoint} of '{context.Request.RelativePath}'.");

        if (context.ComparisonInstance is null)
        {
            await using Stream body = await runArtifactStore
                .OpenReadAsync(artifact.Artifact, cancellationToken)
                .ConfigureAwait(false);

            PayloadFormat format = PayloadFormatDetector.Detect(artifact.ContentType, context.Request.RelativePath)
                ?? PayloadFormat.Json;

            context.ComparisonInstance = await serializer
                .DeserializeAsync(
                    definition.ComparisonType,
                    body,
                    format,
                    ignoreXmlNamespaces: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            context.ResponseArtifact = await PersistCanonicalAsync(context, artifact, cancellationToken).ConfigureAwait(false);
        }

        await next(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ResponseArtifactMetadata> PersistCanonicalAsync(
        IEndpointPipelineContext context,
        ResponseArtifactMetadata rawArtifact,
        CancellationToken cancellationToken)
    {
        await using MemoryStream canonicalBody = new MemoryStream();
        await serializer
            .SerializeAsync(
                context.ComparisonInstance!,
                definition.ComparisonType,
                PayloadFormat.Json,
                canonicalBody,
                cancellationToken)
            .ConfigureAwait(false);
        canonicalBody.Position = 0;

        return await ResponsePersistenceMiddleware.PersistAsync(
            runArtifactStore,
            runId,
            context.Endpoint,
            CreateCanonicalArtifactRequest(context.Request, context.Endpoint),
            rawArtifact.StatusCode,
            "application/json",
            canonicalBody,
            maskRules,
            counters,
            cancellationToken).ConfigureAwait(false);
    }

    // The canonical/ prefix is what run retention and the report use to tell a
    // mapped artifact apart from the raw response it came from.
    private static RequestItem CreateCanonicalArtifactRequest(RequestItem request, EndpointSlot endpoint) =>
        new RequestItem(
            $"canonical/{endpoint}/{request.RelativePath}",
            "application/json",
            request.ContentLength,
            request.Headers,
            request.HeadersA,
            request.HeadersB);
}
