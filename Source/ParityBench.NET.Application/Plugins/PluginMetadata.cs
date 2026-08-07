using ParityBench.NET.Domain.Comparison;

using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Plugin;
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
    IReadOnlyList<PluginProfileTemplate> ProfileTemplates,
    // On-disk package directory, used to resolve package-relative paths (e.g. a
    // template's sample request directory). Empty when not applicable.
    string PackageDirectory = "",
    // The SDK contract version the package's manifest declares, shown in the catalog
    // so a client can see what their package was built against.
    int SdkVersion = PluginManifest.CurrentSdkVersion,
    // False when a higher version of the same plugin id is also installed, and so
    // this one is not what runs unless a profile names its version explicitly.
    bool IsActive = true);

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
    /// Re-reads the plugin directories from disk and then lists what they now hold.
    /// This is what picks up a plugin a client installed, removed or rebuilt while the
    /// app was running; <see cref="GetCatalogAsync"/> reports the last scan.
    /// </summary>
    Task<PluginCatalogView> RefreshCatalogAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets one installed plugin's metadata, or null when it is not installed.
    /// </summary>
    Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the CLR type and baseline comparison rules for one of the plugin's
    /// comparisons (<see cref="ParityBench.PluginSdk.Comparisons.IComparisonDefinition.ComparisonType"/>
    /// and <see cref="ParityBench.PluginSdk.Comparisons.IComparisonDefinition.DefaultComparisonRules"/>),
    /// or null when the plugin or comparison is not found. This is a narrow escape
    /// hatch from the otherwise type-free metadata above: the host UI needs a real
    /// <see cref="Type"/> to reflect over for property-path pickers (ignore/mask
    /// rules) and the baseline rules to preview, the same way it already does for
    /// the built-in response models and contract profiles.
    /// </summary>
    Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// The CLR type and baseline comparison rules a plugin comparison declares. See
/// <see cref="IPluginMetadataProvider.ResolveComparisonDefinitionAsync"/>.
/// </summary>
public sealed record PluginComparisonDefinitionInfo(Type ComparisonType, ComparisonRuleDefaults DefaultComparisonRules);
