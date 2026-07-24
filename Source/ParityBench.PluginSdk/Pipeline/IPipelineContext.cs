using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Configuration;

namespace ParityBench.PluginSdk.Pipeline;

/// <summary>
/// State shared by both pipeline scopes.
/// </summary>
public interface IPipelineContext
{
    /// <summary>Gets the id of the run this pair belongs to.</summary>
    string RunId { get; }

    /// <summary>Gets the source request being executed.</summary>
    RequestItem Request { get; }

    /// <summary>Gets the run-scoped service provider, including plugin-registered services.</summary>
    IServiceProvider Services { get; }

    /// <summary>Gets the profile-supplied configuration, addressable per step id.</summary>
    IPipelineConfiguration Configuration { get; }

    /// <summary>Gets a scratch bag for passing state between steps of the same run scope.</summary>
    IDictionary<string, object?> Items { get; }

    /// <summary>Gets a value indicating whether a step has failed this scope.</summary>
    bool IsFailed { get; }

    /// <summary>Gets the reason recorded by the first failing step.</summary>
    string? FailureReason { get; }

    /// <summary>
    /// Records a failure. The first reason wins; later calls are ignored so the
    /// original cause is never masked by fallout from it.
    /// </summary>
    void Fail(string reason);
}

/// <summary>
/// Endpoint-scoped state, mutated as the request flows Input → Mapping for one
/// endpoint slot.
/// </summary>
public interface IEndpointPipelineContext : IPipelineContext
{
    /// <summary>Gets the slot (A or B) this invocation is for.</summary>
    EndpointSlot Endpoint { get; }

    /// <summary>Gets the endpoint being called, including its configured headers.</summary>
    EndpointDefinition EndpointDefinition { get; }

    /// <summary>Gets the detected format of the source request on disk.</summary>
    PayloadFormat SourceFormat { get; }

    /// <summary>Gets the content type of the source request on disk.</summary>
    string SourceContentType { get; }

    /// <summary>Opens the source request body. Callers own the returned stream.</summary>
    ValueTask<Stream> OpenSourceRequestBodyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the outbound request body. Replacing this disposes nothing —
    /// a step that swaps the body owns disposal of what it replaced.
    /// </summary>
    ContractPayload? RequestBody { get; set; }

    /// <summary>Gets the mutable outbound header set, already merged from endpoint and request.</summary>
    IDictionary<string, string> RequestHeaders { get; }

    /// <summary>Gets or sets the transport result. Set by the transport phase.</summary>
    PipelineTransportResponse? Response { get; set; }

    /// <summary>Gets or sets the persisted response artifact. Set by the response phase.</summary>
    ResponseArtifactMetadata? ResponseArtifact { get; set; }

    /// <summary>
    /// Opens the persisted raw response for reading. Available in the mapping phase
    /// onwards (the response phase sets it), so a plugin's mapping step can read the
    /// response body without being handed the artifact store. Callers own the stream.
    /// </summary>
    ValueTask<Stream> OpenResponseArtifactAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets or sets the instance of the comparison type this slot produced.
    /// Set by the mapping phase; both slots must produce the same CLR type.
    /// </summary>
    object? ComparisonInstance { get; set; }
}

/// <summary>
/// Pair-scoped state for the comparison and result-processing phases.
/// </summary>
public interface IPairPipelineContext : IPipelineContext
{
    /// <summary>Gets the comparison instance produced by endpoint A's mapping phase.</summary>
    object? ComparisonA { get; }

    /// <summary>Gets the comparison instance produced by endpoint B's mapping phase.</summary>
    object? ComparisonB { get; }

    /// <summary>Gets the persisted response artifact for endpoint A.</summary>
    ResponseArtifactMetadata? ResponseArtifactA { get; }

    /// <summary>Gets the persisted response artifact for endpoint B.</summary>
    ResponseArtifactMetadata? ResponseArtifactB { get; }

    /// <summary>Gets the comparison settings resolved for this run.</summary>
    ComparisonOptions ComparisonOptions { get; }

    /// <summary>Gets the mutable comparison outcome for this pair.</summary>
    PairComparisonResult Result { get; }
}
