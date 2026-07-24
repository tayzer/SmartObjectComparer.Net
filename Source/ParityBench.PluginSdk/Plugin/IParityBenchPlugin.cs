namespace ParityBench.PluginSdk.Plugin;

/// <summary>
/// A client plugin's entry point. The loader instantiates the single public
/// implementation found in the package's entry assembly and calls
/// <see cref="Configure"/> once per run scope.
/// </summary>
public interface IParityBenchPlugin
{
    /// <summary>
    /// Gets the plugin id, which must match the id declared in the package manifest.
    /// </summary>
    string PluginId { get; }

    /// <summary>
    /// Registers comparisons, middleware, services, configuration schemas,
    /// environments and profile templates.
    /// </summary>
    void Configure(IPluginBuilder builder);
}
