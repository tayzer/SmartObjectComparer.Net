using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// The baseline library as hosts see it: browse, inspect, move packages between
/// machines and delete them. Capture is deliberately absent — packages are only ever
/// written by a capture run.
/// </summary>
public interface IBaselineLibraryUseCases
{
    Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<BaselinePackageManifest?> GetAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default);

    Task ExportAsync(
        BaselineId id,
        int version,
        string archivePath,
        CancellationToken cancellationToken = default);

    Task<BaselinePackageManifest> ImportAsync(
        string archivePath,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default);
}

public sealed class BaselineLibraryService : IBaselineLibraryUseCases
{
    private readonly IBaselineStore store;

    public BaselineLibraryService(IBaselineStore store)
    {
        this.store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public Task<IReadOnlyList<BaselineSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        store.ListAsync(cancellationToken);

    public Task<BaselinePackageManifest?> GetAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default) =>
        store.LoadManifestAsync(id, version, cancellationToken);

    public Task ExportAsync(
        BaselineId id,
        int version,
        string archivePath,
        CancellationToken cancellationToken = default) =>
        store.ExportAsync(id, version, archivePath, cancellationToken);

    public Task<BaselinePackageManifest> ImportAsync(
        string archivePath,
        CancellationToken cancellationToken = default) =>
        store.ImportAsync(archivePath, cancellationToken);

    public Task DeleteAsync(
        BaselineId id,
        int? version = null,
        CancellationToken cancellationToken = default) =>
        store.DeleteAsync(id, version, cancellationToken);
}
