using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Stages and reads request batches without exposing workspace file-system layout.
/// </summary>
public interface IRequestBatchStore
{
    /// <summary>
    /// Stages eligible request files under a batch reference and returns the persisted manifest.
    /// </summary>
    Task<RequestBatchManifest> StageDirectoryAsync(
        string sourceDirectory,
        RequestBatchReference batchReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages explicit eligible request files under a batch reference and returns the persisted manifest.
    /// </summary>
    Task<RequestBatchManifest> StageFilesAsync(
        string sourceDirectory,
        IReadOnlyList<string> sourceFiles,
        RequestBatchReference batchReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a staged request-batch manifest.
    /// </summary>
    Task<RequestBatchManifest> LoadManifestAsync(
        RequestBatchReference batchReference,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens a staged request body stream for execution.
    /// </summary>
    Task<Stream> OpenRequestBodyAsync(
        RequestBatchReference batchReference,
        RequestItem request,
        CancellationToken cancellationToken = default);
}
