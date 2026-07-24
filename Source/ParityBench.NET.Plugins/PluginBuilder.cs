using Microsoft.Extensions.DependencyInjection;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Plugin;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.NET.Plugins;

/// <summary>
/// Collects what a plugin registers during <see cref="IParityBenchPlugin.Configure"/>.
/// </summary>
public sealed class PluginBuilder : IPluginBuilder
{
    private readonly List<IComparisonDefinition> comparisons = new List<IComparisonDefinition>();
    private readonly List<Type> middlewareTypes = new List<Type>();
    private readonly List<IComparisonMiddleware> middlewareInstances = new List<IComparisonMiddleware>();
    private readonly List<PluginConfigurationSchema> configurationSchemas = new List<PluginConfigurationSchema>();
    private readonly List<PluginEnvironment> environments = new List<PluginEnvironment>();
    private readonly List<PluginProfileTemplate> profileTemplates = new List<PluginProfileTemplate>();
    private readonly Dictionary<Type, object> comparisonConfigurators = new Dictionary<Type, object>();

    public IServiceCollection Services { get; } = new ServiceCollection();

    public IReadOnlyList<IComparisonDefinition> Comparisons => comparisons;

    public IReadOnlyList<Type> MiddlewareTypes => middlewareTypes;

    public IReadOnlyList<IComparisonMiddleware> MiddlewareInstances => middlewareInstances;

    public IReadOnlyList<PluginConfigurationSchema> ConfigurationSchemas => configurationSchemas;

    public IReadOnlyList<PluginEnvironment> Environments => environments;

    public IReadOnlyList<PluginProfileTemplate> ProfileTemplates => profileTemplates;

    public IReadOnlyDictionary<Type, object> ComparisonConfigurators => comparisonConfigurators;

    public IPluginBuilder AddComparison<TComparison>(IComparisonDefinition<TComparison> definition)
        where TComparison : class
    {
        ArgumentNullException.ThrowIfNull(definition);

        if (definition.ComparisonType != typeof(TComparison))
        {
            throw new InvalidOperationException(
                $"Comparison '{definition.ComparisonId}' declares comparison type '{definition.ComparisonType}' but was registered as '{typeof(TComparison)}'.");
        }

        comparisons.Add(definition);
        return this;
    }

    public IPluginBuilder AddMiddleware<TMiddleware>()
        where TMiddleware : class, IComparisonMiddleware
    {
        // Registered as a concrete service so the run scope can inject the plugin's
        // own services into it.
        Services.AddTransient<TMiddleware>();
        middlewareTypes.Add(typeof(TMiddleware));
        return this;
    }

    public IPluginBuilder AddMiddleware(IComparisonMiddleware middleware)
    {
        ArgumentNullException.ThrowIfNull(middleware);
        middlewareInstances.Add(middleware);
        return this;
    }

    public IPluginBuilder AddComparisonConfigurator<TComparison>(IComparisonConfigurator<TComparison> configurator)
        where TComparison : class
    {
        ArgumentNullException.ThrowIfNull(configurator);
        comparisonConfigurators[typeof(TComparison)] = configurator;
        return this;
    }

    public IPluginBuilder AddConfigurationSchema(PluginConfigurationSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);
        configurationSchemas.Add(schema);
        return this;
    }

    public IPluginBuilder AddEnvironment(PluginEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(environment);
        environments.Add(environment);
        return this;
    }

    public IPluginBuilder AddProfileTemplate(PluginProfileTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);
        profileTemplates.Add(template);
        return this;
    }
}
