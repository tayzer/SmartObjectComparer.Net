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

    public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        List<InstalledPluginMetadata> plugins = new List<InstalledPluginMetadata>();
        List<PluginInstallationFailure> failures = catalog.Failures
            .Select(failure => new PluginInstallationFailure(failure.DirectoryPath, failure.Reason))
            .ToList();

        foreach (PluginPackage package in catalog.Packages)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                plugins.Add(ReadMetadata(package));
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                failures.Add(new PluginInstallationFailure(package.DirectoryPath, exception.Message));
            }
        }

        return Task.FromResult(new PluginCatalogView(plugins, failures));
    }

    public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGet(pluginId, version, out PluginPackage? package))
        {
            return Task.FromResult<InstalledPluginMetadata?>(null);
        }

        return Task.FromResult<InstalledPluginMetadata?>(ReadMetadata(package!));
    }

    public Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default)
    {
        if (!catalog.TryGet(pluginId, version, out PluginPackage? package))
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

    private InstalledPluginMetadata ReadMetadata(PluginPackage package)
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
            package.DirectoryPath);
    }
}
