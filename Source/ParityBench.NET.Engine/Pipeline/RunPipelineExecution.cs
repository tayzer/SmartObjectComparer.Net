using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline.BuiltIn;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline;

/// <summary>
/// The pipeline a single run executes, plus the run-scoped state its contexts need.
/// Built once per run: the built-in steps are combined with the plugin steps the
/// plan supplied, and the builder enforces phase ordering over the union.
/// </summary>
public sealed class RunPipelineExecution
{
    private readonly ComparisonExecutionPlan plan;
    private readonly RunOptions comparisonOptions;

    private RunPipelineExecution(
        ComparisonExecutionPlan plan,
        RunOptions comparisonOptions,
        ComparisonPipeline pipeline)
    {
        this.plan = plan;
        this.comparisonOptions = comparisonOptions;
        Pipeline = pipeline;
    }

    public ComparisonPipeline Pipeline { get; }

    public static RunPipelineExecution Create(
        ComparisonExecutionPlan plan,
        ComparisonRun run,
        RunOptions comparisonOptions,
        IEndpointRequestSender endpointRequestSender,
        IRunArtifactStore runArtifactStore,
        IContractPayloadSerializer? contractPayloadSerializer,
        RunExecutionCounters counters,
        DetailedCompareMetricsCollector? timing = null)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(comparisonOptions);

        IContractPayloadSerializer serializer = contractPayloadSerializer
            ?? throw new InvalidOperationException("A contract payload serializer is required to run a plugin comparison.");

        IReadOnlyList<MaskRuleDefinition> maskRules = comparisonOptions.Comparison.MaskRules;

        ComparisonPipeline pipeline = new ComparisonPipelineBuilder()
            .Add(new SourceRequestLoaderMiddleware(plan.Definition, comparisonOptions.RequestExecution.ContentTypeOverride))
            .Add(new HeaderMergeMiddleware(plan.Definition))
            .Add(new HttpTransportMiddleware(endpointRequestSender, comparisonOptions.Timeout))
            .Add(new ResponsePersistenceMiddleware(runArtifactStore, run.Id, maskRules, counters))
            .Add(new CanonicalMappingMiddleware(plan.Definition, serializer, runArtifactStore, run.Id, maskRules, counters, timing))
            .Add(new CompareNetObjectsMiddleware(timing))
            .AddRange(plan.PluginSteps)
            .Build(timing);

        return new RunPipelineExecution(plan, comparisonOptions, pipeline);
    }

    public EndpointPipelineContext CreateEndpointContext(
        ComparisonRun run,
        RequestItem request,
        EndpointSlot endpoint,
        EndpointDefinition endpointDefinition,
        Func<CancellationToken, Task<Stream>> openSourceRequestBodyAsync)
    {
        string sourceContentType = comparisonOptions.RequestExecution.ContentTypeOverride ?? request.ContentType;

        return new EndpointPipelineContext(
            run.Id.Value,
            request,
            endpoint,
            endpointDefinition,
            // Requests with no recognisable format are handed to the pipeline as text
            // rather than rejected: a plugin request step may know how to read them.
            PayloadFormatDetector.Detect(sourceContentType, request.RelativePath) ?? Domain.ContractProfiles.PayloadFormat.Text,
            sourceContentType,
            async token => await openSourceRequestBodyAsync(token).ConfigureAwait(false),
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            plan.Services,
            plan.Configuration);
    }

    public PairPipelineContext CreatePairContext(
        ComparisonRun run,
        RequestItem request,
        IEndpointPipelineContext endpointA,
        IEndpointPipelineContext endpointB,
        ComparisonOptions options) =>
        new PairPipelineContext(
            run.Id.Value,
            request,
            endpointA,
            endpointB,
            options,
            plan.Services,
            plan.Configuration);
}
