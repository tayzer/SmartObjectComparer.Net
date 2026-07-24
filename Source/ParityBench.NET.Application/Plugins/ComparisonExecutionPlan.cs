using ParityBench.NET.Domain.Runs;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Application.Plugins;

/// <summary>
/// Everything a run needs from the plugin side: the comparison it selected, the
/// plugin steps to splice into the pipeline, the configuration those steps read,
/// and the dependency-injection scope they were built from.
/// </summary>
/// <remarks>
/// Disposing the plan disposes the run's plugin scope. Because plugin code is
/// loaded in a collectible load context inside the worker process, that scope is
/// what bounds a plugin's lifetime to the run that asked for it.
/// </remarks>
public sealed class ComparisonExecutionPlan : IAsyncDisposable
{
    private readonly IAsyncDisposable? scope;

    public ComparisonExecutionPlan(
        IComparisonDefinition definition,
        IReadOnlyList<IComparisonMiddleware> pluginSteps,
        IPipelineConfiguration configuration,
        IServiceProvider services,
        IAsyncDisposable? scope = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(pluginSteps);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(services);

        Definition = definition;
        PluginSteps = pluginSteps;
        Configuration = configuration;
        Services = services;
        this.scope = scope;
    }

    public IComparisonDefinition Definition { get; }

    public IReadOnlyList<IComparisonMiddleware> PluginSteps { get; }

    public IPipelineConfiguration Configuration { get; }

    public IServiceProvider Services { get; }

    public ValueTask DisposeAsync() => scope?.DisposeAsync() ?? ValueTask.CompletedTask;
}

/// <summary>
/// Resolves the plan for a run. Implemented over the plugin catalog, so the engine
/// never learns how plugins are packaged, loaded or isolated.
/// </summary>
public interface IComparisonPlanFactory
{
    /// <summary>
    /// Builds the plan for a run, or returns null when the run selected no plugin
    /// comparison (a raw response-to-response comparison).
    /// </summary>
    Task<ComparisonExecutionPlan?> CreateAsync(RunOptions options, CancellationToken cancellationToken = default);
}
