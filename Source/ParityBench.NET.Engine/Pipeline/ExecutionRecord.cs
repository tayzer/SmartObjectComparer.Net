using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine.Pipeline;

public sealed record EndpointExecutionRecord(
    EndpointSlot Endpoint,
    ResponseArtifactMetadata? Metadata,
    string? ErrorMessage)
{
    public int? StatusCode => Metadata?.StatusCode;

    public string? ContentType => Metadata?.ContentType;

    public bool IsSuccessStatusCode => StatusCode is >= 200 and <= 299;

    public static EndpointExecutionRecord Persisted(EndpointSlot endpoint, ResponseArtifactMetadata metadata) =>
        new EndpointExecutionRecord(endpoint, metadata, null);

    public static EndpointExecutionRecord Failure(EndpointSlot endpoint, string errorMessage) =>
        new EndpointExecutionRecord(endpoint, null, errorMessage);
}

public sealed record ExecutionRecord(
    int ManifestOrdinal,
    RequestItem Request,
    EndpointExecutionRecord EndpointA,
    EndpointExecutionRecord EndpointB);