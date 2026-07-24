using System.Reflection;
using System.Runtime.Loader;

namespace ParityBench.NET.Plugins;

/// <summary>
/// One collectible load context per plugin package, so two plugins can depend on
/// different versions of the same library without either winning.
/// </summary>
/// <remarks>
/// The SDK is deliberately <em>not</em> loaded into the plugin's context: types
/// like <c>IComparisonMiddleware</c> must unify with the host's, or a plugin's
/// middleware would not be assignable to the interface the pipeline expects. Every
/// other dependency resolves from the package's own <c>.deps.json</c> first, and
/// only falls back to the default context when the package does not carry it.
/// </remarks>
public sealed class PluginLoadContext : AssemblyLoadContext
{
    // Assemblies that form the plugin contract. These always come from the host so
    // the type identity is shared across every loaded plugin.
    private static readonly string[] SharedAssemblyPrefixes =
    {
        "ParityBench.PluginSdk",
        "ParityBench.NET.Domain",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
    };

    private readonly AssemblyDependencyResolver resolver;

    public PluginLoadContext(string entryAssemblyPath, string name)
        : base(name, isCollectible: true)
    {
        resolver = new AssemblyDependencyResolver(entryAssemblyPath);
    }

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        if (IsShared(assemblyName))
        {
            return null;
        }

        string? assemblyPath = resolver.ResolveAssemblyToPath(assemblyName);
        return assemblyPath is null ? null : LoadFromAssemblyPath(assemblyPath);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? libraryPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return libraryPath is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(libraryPath);
    }

    private static bool IsShared(AssemblyName assemblyName) =>
        assemblyName.Name is string name
        && SharedAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal));
}
