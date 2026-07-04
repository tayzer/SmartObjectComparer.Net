using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Persists run artifacts while keeping artifact references storage-neutral.
/// </summary>
public interface IRunArtifactStore
{
    /// <summary>
    /// Saves a response body stream and returns metadata needed for basic comparison.
    /// </summary>
    Task<ResponseArtifactMetadata> SaveResponseAsync(
        RunId runId,
        EndpointSlot endpoint,
        RequestItem request,
        int statusCode,
        string? contentType,
        Stream body,
        CancellationToken cancellationToken = default);
}
