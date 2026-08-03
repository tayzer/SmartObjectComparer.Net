using System.Reflection;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins;

/// <summary>
/// A plugin package with its code loaded and its registrations collected.
/// </summary>
public sealed class LoadedPlugin
{
    internal LoadedPlugin(PluginPackage package, PluginLoadContext loadContext, PluginBuilder registrations, string shadowDirectoryPath)
    {
        Package = package;
        LoadContext = loadContext;
        Registrations = registrations;
        ShadowDirectoryPath = shadowDirectoryPath;
    }

    /// <summary>
    /// Gets the installed package, not the copy the assemblies were loaded from.
    /// Package-relative paths resolved from here (a profile template's sample request
    /// directory, for one) are persisted into saved run profiles, so they must point at
    /// the installation rather than at a copy that disappears when the app exits.
    /// </summary>
    public PluginPackage Package { get; }

    public PluginLoadContext LoadContext { get; }

    public PluginBuilder Registrations { get; }

    /// <summary>Gets the private copy of the package the load context memory-mapped.</summary>
    public string ShadowDirectoryPath { get; }
}

/// <summary>
/// Loads plugin packages into isolated load contexts and runs their entry points.
/// </summary>
/// <remarks>
/// <para>
/// Loading executes plugin code, and it happens wherever the loader lives — including
/// the desktop process, which reads plugin metadata for the catalog UI directly.
/// Enabling worker-process execution swaps out the run executor only; it does not move
/// metadata reads out of the host.
/// </para>
/// <para>
/// Assemblies are loaded from a private copy of the package, so a client can rebuild a
/// plugin in place while the app is running. A package whose contents changed since it
/// was loaded is evicted and reloaded on the next request. Eviction marks the old load
/// context for collection but never tears it down: a run that already resolved its
/// comparison keeps working from the objects it holds.
/// </para>
/// </remarks>
public sealed class PluginLoader : IDisposable
{
    private readonly Dictionary<string, LoadedPlugin> loadedPlugins = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
    private readonly List<string> pendingShadowDeletions = new List<string>();
    private readonly string shadowRoot;
    private readonly object gate = new object();
    private string? sessionDirectory;
    private bool disposed;

    public PluginLoader()
        : this(null)
    {
    }

    /// <param name="shadowRootPath">
    /// Where private copies of plugin packages are written. Defaults to a folder under
    /// the temp directory; tests and locked-down environments can redirect it.
    /// </param>
    public PluginLoader(string? shadowRootPath)
    {
        shadowRoot = string.IsNullOrWhiteSpace(shadowRootPath) ? PluginShadowCopy.DefaultRoot : shadowRootPath;
    }

    public LoadedPlugin Load(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            DrainPendingShadowDeletions();

            string identity = $"{package.Manifest.Id}::{package.Manifest.Version}";
            if (loadedPlugins.TryGetValue(identity, out LoadedPlugin? cached))
            {
                if (string.Equals(cached.Package.ContentStamp, package.ContentStamp, StringComparison.Ordinal))
                {
                    return cached;
                }

                // Same id and version, different files: the client rebuilt it.
                Evict(identity, cached);
            }

            LoadedPlugin loaded = LoadCore(package, EnsureSessionDirectory());
            loadedPlugins[identity] = loaded;
            return loaded;
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;

            foreach (KeyValuePair<string, LoadedPlugin> entry in loadedPlugins.ToArray())
            {
                Evict(entry.Key, entry.Value);
            }

            DrainPendingShadowDeletions();

            if (sessionDirectory is not null)
            {
                PluginShadowCopy.TryDelete(sessionDirectory);
            }
        }
    }

    /// <summary>
    /// Drops a loaded plugin so the next request reloads it.
    /// </summary>
    /// <remarks>
    /// This must only forget the entry, mark the context collectible and queue the copy
    /// for deletion. Disposing the registrations or their services would break a run
    /// that is mid-flight: a run resolves its comparison and steps once and then holds
    /// them itself, and those references are also what keep the old context alive until
    /// the run finishes.
    /// </remarks>
    private void Evict(string identity, LoadedPlugin evicted)
    {
        loadedPlugins.Remove(identity);

        try
        {
            evicted.LoadContext.Unload();
        }
        catch (InvalidOperationException)
        {
            // Already unloading, or not collectible. Nothing to do either way.
        }

        pendingShadowDeletions.Add(evicted.ShadowDirectoryPath);
    }

    /// <summary>
    /// Retries deleting the copies of evicted packages. Most will still be mapped and
    /// stay until the process exits, which is why this is opportunistic.
    /// </summary>
    private void DrainPendingShadowDeletions()
    {
        for (int index = pendingShadowDeletions.Count - 1; index >= 0; index--)
        {
            string directory = pendingShadowDeletions[index];
            PluginShadowCopy.TryDelete(directory);
            if (!Directory.Exists(directory))
            {
                pendingShadowDeletions.RemoveAt(index);
            }
        }
    }

    private string EnsureSessionDirectory()
    {
        if (sessionDirectory is not null)
        {
            return sessionDirectory;
        }

        sessionDirectory = PluginShadowCopy.CreateSessionDirectory(shadowRoot);

        // Sessions from a process that crashed before it could clean up are only
        // findable now that we know which one is ours.
        PluginShadowCopy.TryPurgeStaleSessions(shadowRoot, sessionDirectory);

        return sessionDirectory;
    }

    private static LoadedPlugin LoadCore(PluginPackage package, string sessionDirectory)
    {
        string shadowDirectoryPath = PluginShadowCopy.CopyPackage(package, sessionDirectory);
        string shadowEntryAssemblyPath = Path.Combine(shadowDirectoryPath, package.Manifest.EntryAssembly);

        PluginLoadContext loadContext = new PluginLoadContext(
            shadowEntryAssemblyPath,
            $"ParityBenchPlugin:{package.Manifest.Id}:{package.Manifest.Version}:{package.ContentStamp}");

        Assembly entryAssembly = loadContext.LoadFromAssemblyPath(shadowEntryAssemblyPath);

        Type pluginType = entryAssembly
            .GetTypes()
            .Where(type => typeof(IParityBenchPlugin).IsAssignableFrom(type) && type is { IsAbstract: false, IsInterface: false })
            .SingleOrDefault()
            ?? throw new InvalidOperationException(
                $"Plugin package '{package.Manifest.Id}' must contain exactly one public {nameof(IParityBenchPlugin)} implementation in '{package.Manifest.EntryAssembly}'.");

        IParityBenchPlugin plugin = (IParityBenchPlugin)Activator.CreateInstance(pluginType)!;

        if (!string.Equals(plugin.PluginId, package.Manifest.Id, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Plugin entry point reports id '{plugin.PluginId}' but its manifest declares '{package.Manifest.Id}'.");
        }

        PluginBuilder registrations = new PluginBuilder();
        plugin.Configure(registrations);

        return new LoadedPlugin(package, loadContext, registrations, shadowDirectoryPath);
    }
}
