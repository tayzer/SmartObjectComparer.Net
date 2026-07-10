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

    /// <summary>
    /// Opens a previously saved artifact body by its logical reference.
    /// </summary>
    Task<Stream> OpenReadAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns true when the artifact exists in the backing store.
    /// </summary>
    Task<bool> ExistsAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);

    /// <summary>
    /// Returns the artifact content length in bytes when present.
    /// </summary>
    Task<long> GetArtifactSizeAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);

    /// <summary>
    /// Returns the current total bytes used by persisted run artifacts.
    /// </summary>
    Task<long> GetTotalArtifactBytesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(0L);

    /// <summary>
    /// Deletes an artifact if present.
    /// </summary>
    Task<bool> DeleteIfExistsAsync(
        ArtifactReference artifact,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(false);
}
