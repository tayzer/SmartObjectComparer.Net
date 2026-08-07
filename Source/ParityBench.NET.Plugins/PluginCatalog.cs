using System.Text.Json;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins;

/// <summary>
/// An installed plugin package on disk, described by its manifest alone. Building
/// one loads no plugin code.
/// </summary>
/// <param name="ContentStamp">
/// A fingerprint of the package's files as of the scan that found it. Two packages
/// with the same id and version but different stamps are different builds — this is
/// what lets the loader notice a plugin rebuilt without a version bump.
/// </param>
public sealed record PluginPackage(string DirectoryPath, PluginManifest Manifest, string ContentStamp = "")
{
    public string EntryAssemblyPath => Path.Combine(DirectoryPath, Manifest.EntryAssembly);
}

/// <summary>
/// What the plugin directories held at one moment. Never changes after construction,
/// so a caller can read packages and failures without them shifting underneath.
/// </summary>
public sealed class PluginCatalogSnapshot
{
    private readonly PluginPackage[] packages;
    private readonly Dictionary<string, PluginPackage[]> packagesById;

    internal PluginCatalogSnapshot(int generation, PluginPackage[] packages, PluginDiscoveryFailure[] failures)
    {
        Generation = generation;
        this.packages = packages;
        Failures = failures;

        // Highest version first, so resolving without a version is a lookup rather
        // than a scan and "which one is live" has a single answer.
        packagesById = packages
            .GroupBy(package => package.Manifest.Id, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderByDescending(package => package.Manifest.Version, PluginVersionOrder.Instance)
                    .ToArray(),
                StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Gets a number that increases with every rescan. Useful for spotting a stale read.</summary>
    public int Generation { get; }

    /// <summary>Gets the usable packages, in discovery order.</summary>
    public IReadOnlyList<PluginPackage> Packages => packages;

    /// <summary>
    /// Gets the packages that were found but rejected, with the reason. Surfaced in
    /// the plugin catalog UI so a client can see <em>why</em> their package did not
    /// appear instead of it silently missing.
    /// </summary>
    public IReadOnlyList<PluginDiscoveryFailure> Failures { get; }

    /// <summary>
    /// Finds a package by id, and by version when one is named. Without a version the
    /// highest installed one wins.
    /// </summary>
    public bool TryGet(string pluginId, string? version, out PluginPackage? package)
    {
        package = null;
        if (string.IsNullOrWhiteSpace(pluginId) || !packagesById.TryGetValue(pluginId, out PluginPackage[]? candidates))
        {
            return false;
        }

        package = version is null
            ? candidates[0]
            : candidates.FirstOrDefault(candidate => string.Equals(candidate.Manifest.Version, version, StringComparison.OrdinalIgnoreCase));

        return package is not null;
    }

    /// <summary>Gets the versions installed for a plugin id, highest first.</summary>
    public IReadOnlyList<string> InstalledVersions(string pluginId) =>
        packagesById.TryGetValue(pluginId, out PluginPackage[]? candidates)
            ? candidates.Select(candidate => candidate.Manifest.Version).ToArray()
            : Array.Empty<string>();

    /// <summary>
    /// Gets whether this package is the one that runs when its id is used without a
    /// version — false for a package superseded by a higher installed version.
    /// </summary>
    public bool IsActive(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        return packagesById.TryGetValue(package.Manifest.Id, out PluginPackage[]? candidates)
            && ReferenceEquals(candidates[0], package);
    }
}

/// <summary>
/// Discovers installed plugin packages by reading manifests. A malformed or
/// incompatible package is skipped with a reason rather than crashing discovery,
/// so one bad package cannot stop the app from listing the others.
/// </summary>
/// <remarks>
/// Discovery can be repeated: <see cref="Rescan"/> re-reads every directory and swaps
/// in a new snapshot, which is how the catalog UI picks up a plugin a client installed
/// or rebuilt while the app was running.
/// </remarks>
public sealed class PluginCatalog
{
    private readonly string[] pluginDirectories;
    private readonly object gate = new object();
    private PluginCatalogSnapshot snapshot;

    public PluginCatalog(IEnumerable<string> pluginDirectories)
    {
        ArgumentNullException.ThrowIfNull(pluginDirectories);

        this.pluginDirectories = pluginDirectories.ToArray();
        snapshot = Scan(this.pluginDirectories, generation: 0);
    }

    /// <summary>Gets the most recent scan. Read it once and reuse it to keep a consistent view.</summary>
    public PluginCatalogSnapshot Current => Volatile.Read(ref snapshot);

    public IReadOnlyList<PluginPackage> Packages => Current.Packages;

    public IReadOnlyList<PluginDiscoveryFailure> Failures => Current.Failures;

    /// <summary>
    /// Re-reads every plugin directory and replaces the snapshot. Reads only manifests,
    /// so this is cheap enough to run from a UI refresh; no plugin code is loaded.
    /// </summary>
    public PluginCatalogSnapshot Rescan()
    {
        lock (gate)
        {
            PluginCatalogSnapshot rescanned = Scan(pluginDirectories, snapshot.Generation + 1);
            Volatile.Write(ref snapshot, rescanned);
            return rescanned;
        }
    }

    public bool TryGet(string pluginId, string? version, out PluginPackage? package) =>
        Current.TryGet(pluginId, version, out package);

    private static PluginCatalogSnapshot Scan(IReadOnlyList<string> directories, int generation)
    {
        List<PluginPackage> packages = new List<PluginPackage>();
        List<PluginDiscoveryFailure> failures = new List<PluginDiscoveryFailure>();

        foreach (string directory in directories)
        {
            Discover(directory, packages, failures);
        }

        return new PluginCatalogSnapshot(generation, packages.ToArray(), failures.ToArray());
    }

    private static void Discover(string directory, List<PluginPackage> packages, List<PluginDiscoveryFailure> failures)
    {
        // Checked per scan rather than per catalog, so a plugins directory created
        // after start-up is picked up by the next refresh.
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

                if (manifest.SdkVersion != PluginManifest.CurrentSdkVersion)
                {
                    failures.Add(new PluginDiscoveryFailure(
                        packageDirectory,
                        $"Plugin '{manifest.Id}' targets SDK version {manifest.SdkVersion}; this app supports {PluginManifest.CurrentSdkVersion}."));
                    continue;
                }

                PluginPackage package = new PluginPackage(
                    packageDirectory,
                    manifest,
                    PluginPackageStamp.Compute(packageDirectory));

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
