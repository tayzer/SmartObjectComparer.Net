using ParityBench.NET.Application.Plugins;

using ParityBench.PluginSdk.Comparisons;

namespace ParityBench.NET.Plugins;

/// <summary>
/// Reads installed-plugin metadata by loading each package and collecting its
/// registrations.
/// </summary>
/// <remarks>
/// Loading runs the plugin's <c>Configure</c>, so this executes plugin code. The
/// metadata it returns is composed of SDK and primitive types (shared from the
/// default context), so it stays valid independently of the plugin's own types.
/// A package that fails to load is reported as an installation failure rather than
/// throwing, so one bad package cannot hide the rest of the catalog.
/// </remarks>
public sealed class PluginMetadataProvider : IPluginMetadataProvider
{
    private readonly PluginCatalog catalog;
    private readonly PluginLoader loader;

    public PluginMetadataProvider(PluginCatalog catalog, PluginLoader loader)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loader);

        this.catalog = catalog;
        this.loader = loader;
    }

    public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildCatalogView(catalog.Current, cancellationToken));

    public Task<PluginCatalogView> RefreshCatalogAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(BuildCatalogView(catalog.Rescan(), cancellationToken));

    public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default)
    {
        PluginCatalogSnapshot snapshot = catalog.Current;
        if (!snapshot.TryGet(pluginId, version, out PluginPackage? package))
        {
            return Task.FromResult<InstalledPluginMetadata?>(null);
        }

        return Task.FromResult<InstalledPluginMetadata?>(ReadMetadata(package!, snapshot));
    }

    public Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default)
    {
        if (!catalog.Current.TryGet(pluginId, version, out PluginPackage? package))
        {
            return Task.FromResult<PluginComparisonDefinitionInfo?>(null);
        }

        LoadedPlugin loaded = loader.Load(package!);
        IComparisonDefinition? definition = loaded.Registrations.Comparisons
            .FirstOrDefault(comparison => string.Equals(comparison.ComparisonId, comparisonId, StringComparison.OrdinalIgnoreCase));

        return Task.FromResult(definition is null
            ? null
            : new PluginComparisonDefinitionInfo(definition.ComparisonType, definition.DefaultComparisonRules));
    }

    // Everything comes from one snapshot, so the packages and the failures listed
    // together always describe the same moment on disk.
    private PluginCatalogView BuildCatalogView(PluginCatalogSnapshot snapshot, CancellationToken cancellationToken)
    {
        List<InstalledPluginMetadata> plugins = new List<InstalledPluginMetadata>();
        List<PluginInstallationFailure> failures = snapshot.Failures
            .Select(failure => new PluginInstallationFailure(failure.DirectoryPath, failure.Reason))
            .ToList();

        foreach (PluginPackage package in snapshot.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                plugins.Add(ReadMetadata(package, snapshot));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new PluginInstallationFailure(package.DirectoryPath, exception.Message));
            }
        }

        return new PluginCatalogView(plugins, failures);
    }

    private InstalledPluginMetadata ReadMetadata(PluginPackage package, PluginCatalogSnapshot snapshot)
    {
        LoadedPlugin loaded = loader.Load(package);
        PluginBuilder registrations = loaded.Registrations;

        IReadOnlyList<PluginComparisonMetadata> comparisons = registrations.Comparisons
            .Select(comparison => new PluginComparisonMetadata(
                comparison.ComparisonId,
                comparison.DisplayName,
                comparison.DefaultStepIds,
                comparison.RequiredStepIds))
            .ToArray();

        return new InstalledPluginMetadata(
            package.Manifest.Id,
            package.Manifest.Version,
            package.Manifest.DisplayName,
            package.Manifest.Description,
            package.Manifest.Publisher,
            comparisons,
            registrations.ConfigurationSchemas,
            registrations.Environments,
            registrations.ProfileTemplates,
            package.DirectoryPath,
            package.Manifest.SdkVersion,
            snapshot.IsActive(package));
    }
}
