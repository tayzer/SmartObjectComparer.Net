using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// Stores captured baseline packages without exposing storage layout. A package is
/// immutable once completed: a new capture under an existing name becomes a new
/// version rather than overwriting what is already there.
/// </summary>
public interface IBaselineStore
{
    /// <summary>
    /// Reserves the next version for a capture and returns the manifest it will be
    /// written under. The reserved version is empty until scenarios are appended.
    /// </summary>
    Task<BaselinePackageManifest> BeginCaptureAsync(
        BaselineCaptureRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds one captured scenario to an in-progress capture. Safe to call
    /// concurrently: capture runs append from the executor's compare pool.
    /// </summary>
    Task<BaselineScenarioEntry> AppendScenarioAsync(
        BaselineId id,
        int version,
        BaselineScenarioCapture scenario,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the manifest for a completed capture, sealing the version.
    /// </summary>
    Task<BaselinePackageManifest> CompleteCaptureAsync(
        BaselineId id,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes a reserved version that never produced a usable package, so a failed
    /// capture does not leave an empty baseline in the library.
    /// </summary>
    Task AbandonCaptureAsync(
        BaselineId id,
        int version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists every completed package version, newest capture first.
    /// </summary>
    Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Loads a manifest. A null version resolves to the highest completed version.
    /// </summary>
    Task<BaselinePackageManifest?> LoadManifestAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stored comparison model for a scenario — the expected side of a replay.
    /// </summary>
    Task<Stream> OpenCanonicalAsync(
        BaselineId id,
        int version,
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Opens the stored raw response for a scenario, when one was kept.
    /// </summary>
    Task<Stream> OpenRawAsync(
        BaselineId id,
        int version,
        string relativePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes the package's request files into a directory so the caller can stage
    /// them as a request batch with the normal staging path. Keeps the store's own
    /// layout private.
    /// </summary>
    Task<int> ExportRequestsToDirectoryAsync(
        BaselineId id,
        int version,
        string targetDirectory,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Writes a portable archive of the package to <paramref name="archivePath"/>.
    /// </summary>
    Task ExportAsync(
        BaselineId id,
        int version,
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Imports a portable archive, assigning a fresh version so an import can never
    /// overwrite a package already in the library.
    /// </summary>
    Task<BaselinePackageManifest> ImportAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes one version, or every version when <paramref name="version"/> is null.
    /// </summary>
    Task DeleteAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default);
}
