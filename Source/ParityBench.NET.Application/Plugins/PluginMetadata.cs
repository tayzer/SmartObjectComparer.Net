using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.NET.Application.Plugins;

/// <summary>
/// Neutral, host-facing metadata for one comparison a plugin offers. Carries no
/// CLR type — the UI renders and configures a comparison from ids and schemas
/// alone, which is what keeps client-specific UI code out of the product.
/// </summary>
public sealed record PluginComparisonMetadata(
    string ComparisonId,
    string DisplayName,
    IReadOnlyList<string> DefaultStepIds,
    IReadOnlyList<string> RequiredStepIds);

/// <summary>
/// Neutral metadata for one installed plugin package.
/// </summary>
public sealed record InstalledPluginMetadata(
    string PluginId,
    string Version,
    string DisplayName,
    string? Description,
    string? Publisher,
    IReadOnlyList<PluginComparisonMetadata> Comparisons,
    IReadOnlyList<PluginConfigurationSchema> ConfigurationSchemas,
    IReadOnlyList<PluginEnvironment> Environments,
    IReadOnlyList<PluginProfileTemplate> ProfileTemplates);

/// <summary>
/// A package that was found but could not be used, with the reason — surfaced so a
/// client can see why their package did not appear rather than it silently missing.
/// </summary>
public sealed record PluginInstallationFailure(string DirectoryPath, string Reason);

/// <summary>
/// The installed-plugin view the catalog UI renders.
/// </summary>
public sealed record PluginCatalogView(
    IReadOnlyList<InstalledPluginMetadata> Plugins,
    IReadOnlyList<PluginInstallationFailure> Failures);

/// <summary>
/// Reads installed-plugin metadata for the host UI without the UI depending on the
/// plugin-loading implementation.
/// </summary>
public interface IPluginMetadataProvider
{
    /// <summary>
    /// Lists installed plugins and the packages that failed discovery.
    /// </summary>
    Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one installed plugin's metadata, or null when it is not installed.
    /// </summary>
    Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default);
}
