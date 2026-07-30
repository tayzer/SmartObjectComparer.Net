using System.Text.Json;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins;

/// <summary>
/// An installed plugin package on disk, described by its manifest alone. Building
/// one loads no plugin code.
/// </summary>
public sealed record PluginPackage(string DirectoryPath, PluginManifest Manifest)
{
    public string EntryAssemblyPath => Path.Combine(DirectoryPath, Manifest.EntryAssembly);
}

/// <summary>
/// Discovers installed plugin packages by reading manifests. A malformed or
/// incompatible package is skipped with a reason rather than crashing discovery,
/// so one bad package cannot stop the app from listing the others.
/// </summary>
public sealed class PluginCatalog
{
    private readonly List<PluginPackage> packages = new List<PluginPackage>();
    private readonly List<PluginDiscoveryFailure> failures = new List<PluginDiscoveryFailure>();

    public PluginCatalog(IEnumerable<string> pluginDirectories)
    {
        ArgumentNullException.ThrowIfNull(pluginDirectories);

        foreach (string directory in pluginDirectories)
        {
            Discover(directory);
        }
    }

    public IReadOnlyList<PluginPackage> Packages => packages;

    /// <summary>
    /// Gets the packages that were found but rejected, with the reason. Surfaced in
    /// the plugin catalog UI so a client can see <em>why</em> their package did not
    /// appear instead of it silently missing.
    /// </summary>
    public IReadOnlyList<PluginDiscoveryFailure> Failures => failures;

    public bool TryGet(string pluginId, string? version, out PluginPackage? package)
    {
        package = packages.FirstOrDefault(candidate =>
            string.Equals(candidate.Manifest.Id, pluginId, StringComparison.OrdinalIgnoreCase)
            && (version is null || string.Equals(candidate.Manifest.Version, version, StringComparison.OrdinalIgnoreCase)));

        return package is not null;
    }

    private void Discover(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
        {
            return;
        }

        // A plugin root holds one directory per package; a package directory holds
        // the manifest next to the assemblies.
        foreach (string packageDirectory in Directory.EnumerateDirectories(directory))
        {
            string manifestPath = Path.Combine(packageDirectory, PluginManifest.FileName);
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            try
            {
                PluginManifest manifest = JsonSerializer.Deserialize<PluginManifest>(
                    File.ReadAllText(manifestPath),
                    PluginManifest.JsonOptions)
                    ?? throw new InvalidOperationException("Manifest deserialized to null.");

                PluginPackage package = new PluginPackage(packageDirectory, manifest);
                if (manifest.SdkVersion != PluginManifest.CurrentSdkVersion)
                {
                    failures.Add(new PluginDiscoveryFailure(
                        packageDirectory,
                        $"Plugin '{manifest.Id}' targets SDK version {manifest.SdkVersion}; this app supports {PluginManifest.CurrentSdkVersion}."));
                    continue;
                }

                if (!File.Exists(package.EntryAssemblyPath))
                {
                    failures.Add(new PluginDiscoveryFailure(
                        packageDirectory,
                        $"Entry assembly '{manifest.EntryAssembly}' was not found in the package."));
                    continue;
                }

                packages.Add(package);
            }
            catch (Exception ex) when (ex is JsonException or IOException or InvalidOperationException or ArgumentException)
            {
                failures.Add(new PluginDiscoveryFailure(packageDirectory, ex.Message));
            }
        }
    }
}

public sealed record PluginDiscoveryFailure(string DirectoryPath, string Reason);
