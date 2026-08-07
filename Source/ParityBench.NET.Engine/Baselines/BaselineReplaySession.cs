using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Engine.Pipeline.BuiltIn;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Baselines;

/// <summary>
/// Supplies one side of a comparison from a captured baseline instead of calling an
/// endpoint.
/// </summary>
/// <remarks>
/// The replayed slot never enters the request or transport phases, so replaying costs
/// no calls to the captured version — which is the point: by the time a baseline is
/// replayed, that version is usually gone. The stored comparison model is loaded
/// straight into the slot's comparison instance, and persisted into the run's
/// artifacts under the same <c>canonical/</c> naming a live mapped slot uses, so
/// retention and the report cannot tell the two apart.
/// </remarks>
public sealed class BaselineReplaySession
{
    private readonly IBaselineStore store;
    private readonly IRunArtifactStore runArtifactStore;
    private readonly IContractPayloadSerializer serializer;
    private readonly Type comparisonType;
    private readonly IReadOnlyDictionary<string, BaselineScenarioEntry> scenarios;

    private BaselineReplaySession(
        IBaselineStore store,
        IRunArtifactStore runArtifactStore,
        IContractPayloadSerializer serializer,
        Type comparisonType,
        BaselineBinding binding,
        BaselinePackageManifest manifest)
    {
        this.store = store;
        this.runArtifactStore = runArtifactStore;
        this.serializer = serializer;
        this.comparisonType = comparisonType;
        Binding = binding;
        Manifest = manifest;
        scenarios = manifest.Scenarios.ToDictionary(
            scenario => scenario.RelativePath,
            StringComparer.OrdinalIgnoreCase);
    }

    public BaselineBinding Binding { get; }

    public BaselinePackageManifest Manifest { get; }

    public static async Task<BaselineReplaySession> OpenAsync(
        IBaselineStore store,
        IRunArtifactStore runArtifactStore,
        IContractPayloadSerializer? serializer,
        ComparisonExecutionPlan plan,
        BaselineBinding binding,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(runArtifactStore);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(binding);

        if (serializer is null)
        {
            throw new InvalidOperationException("A contract payload serializer is required to replay a baseline.");
        }

        BaselineId id = binding.BaselineId
            ?? throw new InvalidOperationException("A baseline replay run must name the baseline to replay.");
        int version = binding.Version
            ?? throw new InvalidOperationException("A baseline replay run must name the baseline version to replay.");

        BaselinePackageManifest manifest = await store
            .LoadManifestAsync(id, version, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Baseline '{id.Value}' v{version} was not found.");

        return new BaselineReplaySession(
            store,
            runArtifactStore,
            serializer,
            plan.Definition.ComparisonType,
            binding,
            manifest);
    }

    public async Task<EndpointExecutionRecord> ExecuteAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        RunPipelineExecution pipelineExecution,
        Func<CancellationToken, Task<Stream>> openSourceRequestBodyAsync,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        if (!scenarios.TryGetValue(request.RelativePath, out BaselineScenarioEntry? entry))
        {
            // A request the package never saw cannot be judged against it, and silently
            // dropping it would understate the run.
            return EndpointExecutionRecord.Failure(
                endpoint,
                $"Baseline '{Manifest.Name}' {Manifest.DisplayVersion} has no captured scenario for '{request.RelativePath}'.");
        }

        ResponseArtifactMetadata metadata = await PersistCanonicalAsync(
            run,
            comparisonOptions,
            request,
            endpoint,
            entry,
            counters,
            cancellationToken).ConfigureAwait(false);

        EndpointPipelineContext context = pipelineExecution.CreateEndpointContext(
            run,
            request,
            endpoint,
            endpointDefinition,
            openSourceRequestBodyAsync);

        context.ResponseArtifact = metadata;
        context.OpenResponseArtifact = async token =>
            await runArtifactStore.OpenReadAsync(metadata.Artifact, token).ConfigureAwait(false);

        await using (Stream body = await runArtifactStore.OpenReadAsync(metadata.Artifact, cancellationToken).ConfigureAwait(false))
        {
            context.ComparisonInstance = await serializer
                .DeserializeAsync(
                    comparisonType,
                    body,
                    PayloadFormat.Json,
                    ignoreXmlNamespaces: true,
                    cancellationToken)
                .ConfigureAwait(false);
        }

        return EndpointExecutionRecord.FromPipeline(context) with { IsBaselineReplay = true };
    }

    private async Task<ResponseArtifactMetadata> PersistCanonicalAsync(
        ComparisonRun run,
        RunOptions comparisonOptions,
        RequestItem request,
        EndpointSlot endpoint,
        BaselineScenarioEntry entry,
        RunExecutionCounters counters,
        CancellationToken cancellationToken)
    {
        await using Stream canonicalBody = await store
            .OpenCanonicalAsync(Manifest.Id, Manifest.Version, entry.RelativePath, cancellationToken)
            .ConfigureAwait(false);

        // Masked with the run's rules, not the capture's: a mask added since capture
        // has to apply to both sides or every masked field reads as a difference.
        return await ResponsePersistenceMiddleware.PersistAsync(
            runArtifactStore,
            run.Id,
            endpoint,
            CreateCanonicalArtifactRequest(request, endpoint),
            entry.StatusCode,
            "application/json",
            canonicalBody,
            comparisonOptions.Comparison.MaskRules,
            counters,
            cancellationToken).ConfigureAwait(false);
    }

    // Matches CanonicalMappingMiddleware's naming so run retention and the report
    // classify a replayed slot exactly as they classify a live mapped one.
    private static RequestItem CreateCanonicalArtifactRequest(RequestItem request, EndpointSlot endpoint) =>
        new RequestItem(
            $"canonical/{endpoint}/{request.RelativePath}",
            "application/json",
            request.ContentLength,
            request.Headers,
            request.HeadersA,
            request.HeadersB);
}
