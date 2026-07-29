using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Engine.Pipeline;

public sealed record EndpointExecutionRecord(
    EndpointSlot Endpoint,
    ResponseArtifactMetadata? Metadata,
    string? ErrorMessage,
    IEndpointPipelineContext? PipelineContext = null)
{
    /// <summary>
    /// Gets a value indicating whether this slot was supplied from a stored baseline
    /// rather than called. Such a slot already carries its comparison instance, so the
    /// mapping phase must not run over it again.
    /// </summary>
    public bool IsBaselineReplay { get; init; }

    public int? StatusCode => Metadata?.StatusCode;

    public string? ContentType => Metadata?.ContentType;

    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    public static EndpointExecutionRecord Persisted(EndpointSlot endpoint, ResponseArtifactMetadata metadata) =>
        new EndpointExecutionRecord(endpoint, metadata, null);

    /// <summary>
    /// Records a slot that ran through the pipeline. The context is carried to the
    /// compare pool, which finishes the mapping phase against the persisted
    /// artifact — it holds metadata only, never an open response stream.
    /// </summary>
    public static EndpointExecutionRecord FromPipeline(IEndpointPipelineContext context) =>
        context.IsFailed || context.ResponseArtifact is null
            ? new EndpointExecutionRecord(context.Endpoint, context.ResponseArtifact, context.FailureReason ?? "The request pipeline did not persist a response.", context)
            : new EndpointExecutionRecord(context.Endpoint, context.ResponseArtifact, null, context);

    public static EndpointExecutionRecord Failure(EndpointSlot endpoint, string errorMessage) =>
        new EndpointExecutionRecord(endpoint, null, errorMessage);
}

public sealed record ExecutionRecord(
    int ManifestOrdinal,
    RequestItem Request,
    EndpointExecutionRecord EndpointA,
    EndpointExecutionRecord EndpointB);