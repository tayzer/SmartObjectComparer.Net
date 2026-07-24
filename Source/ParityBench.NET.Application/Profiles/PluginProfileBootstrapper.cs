using ParityBench.NET.Application.Plugins;

using ParityBench.PluginSdk.Profiles;

namespace ParityBench.NET.Application.Profiles;

/// <summary>
/// Materialises the profile templates that installed plugins ship into real, saved
/// run profiles, so a freshly installed plugin comes with a selectable profile out
/// of the box.
/// </summary>
/// <remarks>
/// A template is materialised only when no profile with its id already exists, so a
/// profile the operator has edited is never overwritten — the template seeds the
/// profile once and the saved profile owns it thereafter.
/// </remarks>
public sealed class PluginProfileBootstrapper
{
    private readonly IPluginMetadataProvider pluginMetadata;
    private readonly IRunProfileStore profileStore;

    public PluginProfileBootstrapper(IPluginMetadataProvider pluginMetadata, IRunProfileStore profileStore)
    {
        ArgumentNullException.ThrowIfNull(pluginMetadata);
        ArgumentNullException.ThrowIfNull(profileStore);

        this.pluginMetadata = pluginMetadata;
        this.profileStore = profileStore;
    }

    /// <summary>
    /// Ensures every installed plugin's profile templates exist as saved profiles.
    /// Returns the ids of the profiles it created.
    /// </summary>
    public async Task<IReadOnlyList<string>> EnsureTemplateProfilesAsync(CancellationToken cancellationToken = default)
    {
        PluginCatalogView catalog = await pluginMetadata.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RunProfile> existing = await profileStore.ListAsync(cancellationToken).ConfigureAwait(false);
        HashSet<string> existingIds = existing.Select(profile => profile.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

        List<string> created = new List<string>();
        foreach (InstalledPluginMetadata plugin in catalog.Plugins)
        {
            foreach (PluginProfileTemplate template in plugin.ProfileTemplates)
            {
                if (!existingIds.Add(template.TemplateId))
                {
                    continue;
                }

                RunProfile? profile = TryMaterialise(plugin, template);
                if (profile is null)
                {
                    continue;
                }

                await profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
                created.Add(profile.Id);
            }
        }

        return created;
    }

    private static RunProfile? TryMaterialise(InstalledPluginMetadata plugin, PluginProfileTemplate template)
    {
        // Endpoints come from the environment the template names. Without a resolvable
        // environment there is nothing to point the profile at, so it is skipped
        // rather than saved half-formed.
        PluginEnvironment? environment = plugin.Environments
            .FirstOrDefault(candidate => string.Equals(candidate.Name, template.EnvironmentName, StringComparison.OrdinalIgnoreCase));
        if (environment is null)
        {
            return null;
        }

        return new RunProfile(
            template.TemplateId,
            template.DisplayName,
            plugin.PluginId,
            template.ComparisonId,
            environment.EndpointA,
            environment.EndpointB,
            pluginVersion: plugin.Version,
            environmentName: template.EnvironmentName,
            enabledStepIds: template.EnabledStepIds,
            stepConfiguration: template.StepConfiguration,
            requestDirectory: ResolveRequestDirectory(plugin, template));
    }

    // A template's request directory may be package-relative (sample requests shipped
    // inside the package); resolve it to an absolute path against the package on disk.
    private static string? ResolveRequestDirectory(InstalledPluginMetadata plugin, PluginProfileTemplate template)
    {
        if (string.IsNullOrWhiteSpace(template.RequestDirectory))
        {
            return null;
        }

        if (Path.IsPathRooted(template.RequestDirectory) || string.IsNullOrEmpty(plugin.PackageDirectory))
        {
            return template.RequestDirectory;
        }

        return Path.GetFullPath(Path.Combine(plugin.PackageDirectory, template.RequestDirectory));
    }
}
