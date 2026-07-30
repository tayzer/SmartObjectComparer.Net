using System.Reflection;

using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins;

/// <summary>
/// A plugin package with its code loaded and its registrations collected.
/// </summary>
public sealed class LoadedPlugin
{
    internal LoadedPlugin(PluginPackage package, PluginLoadContext loadContext, PluginBuilder registrations)
    {
        Package = package;
        LoadContext = loadContext;
        Registrations = registrations;
    }

    public PluginPackage Package { get; }

    public PluginLoadContext LoadContext { get; }

    public PluginBuilder Registrations { get; }
}

/// <summary>
/// Loads plugin packages into isolated load contexts and runs their entry points.
/// </summary>
/// <remarks>
/// This runs inside the worker process. The desktop app only ever holds the
/// manifest-derived metadata a <see cref="PluginCatalog"/> produces, so a plugin
/// that throws, hangs or conflicts on load can only take the worker down.
/// </remarks>
public sealed class PluginLoader
{
    private readonly Dictionary<string, LoadedPlugin> loadedPlugins = new Dictionary<string, LoadedPlugin>(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new object();

    public LoadedPlugin Load(PluginPackage package)
    {
        ArgumentNullException.ThrowIfNull(package);

        lock (gate)
        {
            string key = $"{package.Manifest.Id}::{package.Manifest.Version}";
            if (loadedPlugins.TryGetValue(key, out LoadedPlugin? cached))
            {
                return cached;
            }

            LoadedPlugin loaded = LoadCore(package);
            loadedPlugins[key] = loaded;
            return loaded;
        }
    }

    private static LoadedPlugin LoadCore(PluginPackage package)
    {
        PluginLoadContext loadContext = new PluginLoadContext(
            package.EntryAssemblyPath,
            $"ParityBenchPlugin:{package.Manifest.Id}:{package.Manifest.Version}");

        Assembly entryAssembly = loadContext.LoadFromAssemblyPath(package.EntryAssemblyPath);

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

        return new LoadedPlugin(package, loadContext, registrations);
    }
}
