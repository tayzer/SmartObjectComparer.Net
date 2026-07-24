using Microsoft.Extensions.DependencyInjection;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.PluginSdk.Plugin;

/// <summary>
/// Fluent registration surface handed to <see cref="IParityBenchPlugin.Configure"/>.
/// </summary>
public interface IPluginBuilder
{
    /// <summary>
    /// Gets the service collection backing the per-run dependency-injection scope.
    /// Plugin services are resolved from here when middleware is constructed.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>
    /// Registers a comparison this plugin offers.
    /// </summary>
    IPluginBuilder AddComparison<TComparison>(IComparisonDefinition<TComparison> definition)
        where TComparison : class;

    /// <summary>
    /// Registers a pipeline step. The implementation type is resolved from the
    /// run scope, so it may take plugin services as constructor dependencies.
    /// </summary>
    IPluginBuilder AddMiddleware<TMiddleware>()
        where TMiddleware : class, IComparisonMiddleware;

    /// <summary>
    /// Registers an already-constructed pipeline step.
    /// </summary>
    IPluginBuilder AddMiddleware(IComparisonMiddleware middleware);

    /// <summary>
    /// Registers per-run comparison-rule tuning for a comparison type.
    /// </summary>
    IPluginBuilder AddComparisonConfigurator<TComparison>(IComparisonConfigurator<TComparison> configurator)
        where TComparison : class;

    /// <summary>
    /// Declares the configuration a step (or the plugin itself) needs, so the app
    /// can render a form for it without knowing anything client-specific.
    /// </summary>
    IPluginBuilder AddConfigurationSchema(PluginConfigurationSchema schema);

    /// <summary>
    /// Declares a named environment (ST, QA, ...) selectable at run time.
    /// </summary>
    IPluginBuilder AddEnvironment(PluginEnvironment environment);

    /// <summary>
    /// Declares a starting point the app materialises into an editable run profile.
    /// </summary>
    IPluginBuilder AddProfileTemplate(PluginProfileTemplate template);
}
