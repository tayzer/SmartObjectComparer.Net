using System.Text.Json;

using ParityBench.NET.Application.Plugins;

using ParityBench.PluginSdk.Profiles;

namespace ParityBench.NET.Application.Profiles;

/// <summary>
/// Materialises the profile templates that installed plugins ship into real, saved
/// run profiles, so a freshly installed plugin comes with a selectable profile out
/// of the box.
/// </summary>
/// <remarks>
/// A template is materialised only when no profile with its id already exists. When
/// a higher plugin version is installed alongside its predecessor, an unchanged
/// profile is refreshed from the higher version's template; an operator-edited
/// profile is never overwritten.
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
    /// Returns the ids of profiles it created or refreshed.
    /// </summary>
    public async Task<IReadOnlyList<string>> EnsureTemplateProfilesAsync(CancellationToken cancellationToken = default)
    {
        PluginCatalogView catalog = await pluginMetadata.GetCatalogAsync(cancellationToken).ConfigureAwait(false);

        IReadOnlyList<RunProfile> existing = await profileStore.ListAsync(cancellationToken).ConfigureAwait(false);
        Dictionary<string, RunProfile> existingById = existing.ToDictionary(profile => profile.Id, StringComparer.OrdinalIgnoreCase);

        List<string> materialised = new List<string>();
        foreach (InstalledPluginMetadata plugin in catalog.Plugins.Where(plugin => plugin.IsActive))
        {
            foreach (PluginProfileTemplate template in plugin.ProfileTemplates)
            {
                RunProfile? profile = TryMaterialise(plugin, template);
                if (profile is null)
                {
                    continue;
                }

                if (existingById.TryGetValue(template.TemplateId, out RunProfile? existingProfile))
                {
                    if (!WasMaterialisedBySupersededVersion(existingProfile, plugin, template, catalog.Plugins))
                    {
                        continue;
                    }
                }

                await profileStore.SaveAsync(profile, cancellationToken).ConfigureAwait(false);
                existingById[profile.Id] = profile;
                materialised.Add(profile.Id);
            }
        }

        return materialised;
    }

    private static bool WasMaterialisedBySupersededVersion(
        RunProfile existingProfile,
        InstalledPluginMetadata activePlugin,
        PluginProfileTemplate activeTemplate,
        IReadOnlyList<InstalledPluginMetadata> installedPlugins)
    {
        // No refresh when current template already describes saved profile. This also
        // makes repeated bootstrapping idempotent.
        RunProfile? activeProfile = TryMaterialise(activePlugin, activeTemplate);
        if (activeProfile is not null && ProfilesMatch(existingProfile, activeProfile))
        {
            return false;
        }

        // A match with a lower installed version is reliable provenance: profile was
        // seeded, then left untouched. Do not infer this from shared ids alone.
        return installedPlugins
            .Where(candidate =>
                !candidate.IsActive
                && string.Equals(candidate.PluginId, activePlugin.PluginId, StringComparison.OrdinalIgnoreCase))
            .SelectMany(candidate => candidate.ProfileTemplates.Select(template => (Plugin: candidate, Template: template)))
            .Where(candidate => string.Equals(candidate.Template.TemplateId, activeTemplate.TemplateId, StringComparison.OrdinalIgnoreCase))
            .Select(candidate => TryMaterialise(candidate.Plugin, candidate.Template))
            .Where(candidate => candidate is not null)
            .Any(candidate => ProfilesMatch(existingProfile, candidate!));
    }

    // RunProfile contains dictionaries and lists, whose record equality is reference
    // based. JSON gives this comparison value semantics over every persisted field.
    private static bool ProfilesMatch(RunProfile left, RunProfile right) =>
        string.Equals(
            JsonSerializer.Serialize(left),
            JsonSerializer.Serialize(right),
            StringComparison.Ordinal);

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
            // Deliberately unpinned: a seeded profile follows the highest installed
            // version of its plugin, so upgrading the package keeps the profile
            // working. Pinning a version is an operator choice made in the editor.
            pluginVersion: null,
            environmentName: template.EnvironmentName,
            enabledStepIds: template.EnabledStepIds,
            stepConfiguration: template.StepConfiguration,
            requestDirectory: ResolveRequestDirectory(plugin, template),
            endpointAHeaders: environment.EndpointAHeaders,
            endpointBHeaders: environment.EndpointBHeaders);
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
