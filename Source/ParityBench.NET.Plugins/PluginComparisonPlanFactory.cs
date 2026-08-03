using Microsoft.Extensions.DependencyInjection;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine.Pipeline;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.NET.Plugins;

/// <summary>
/// Turns a run's plugin selection into an executable plan: the comparison it
/// named, the steps it enabled, and a dependency-injection scope holding both the
/// plugin's services and the host services plugins are allowed to see.
/// </summary>
public sealed class PluginComparisonPlanFactory : IComparisonPlanFactory
{
    private readonly PluginCatalog catalog;
    private readonly PluginLoader loader;
    private readonly Action<IServiceCollection>? configureHostServices;

    public PluginComparisonPlanFactory(
        PluginCatalog catalog,
        PluginLoader loader,
        Action<IServiceCollection>? configureHostServices = null)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(loader);

        this.catalog = catalog;
        this.loader = loader;
        this.configureHostServices = configureHostServices;
    }

    public Task<ComparisonExecutionPlan?> CreateAsync(RunOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (options.PluginComparison is not PluginComparisonSelection selection)
        {
            return Task.FromResult<ComparisonExecutionPlan?>(null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        PluginCatalogSnapshot snapshot = catalog.Current;
        if (!snapshot.TryGet(selection.PluginId, selection.PluginVersion, out PluginPackage? package))
        {
            throw new InvalidOperationException(BuildNotInstalledMessage(snapshot, selection));
        }

        LoadedPlugin loaded = loader.Load(package!);

        IComparisonDefinition definition = loaded.Registrations.Comparisons
            .FirstOrDefault(comparison => string.Equals(comparison.ComparisonId, selection.ComparisonId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                $"Plugin '{selection.PluginId}' does not provide a comparison with id '{selection.ComparisonId}'.");

        ServiceProvider provider = BuildProvider(loaded);
        AsyncServiceScope scope = provider.CreateAsyncScope();

        try
        {
            IReadOnlyList<IComparisonMiddleware> steps = ResolveSteps(loaded, definition, selection, scope.ServiceProvider);

            return Task.FromResult<ComparisonExecutionPlan?>(new ComparisonExecutionPlan(
                definition,
                steps,
                new PipelineConfiguration(selection.StepConfiguration, selection.EnvironmentName),
                scope.ServiceProvider,
                new PluginRunScope(scope, provider)));
        }
        catch
        {
            // The scope owns the plugin's per-run services; a failure while wiring
            // steps must not leave it behind.
            scope.Dispose();
            provider.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Names the versions that <em>are</em> installed when a profile pinned one that is
    /// not, which is the usual shape of this failure after a client upgrades a plugin.
    /// </summary>
    private static string BuildNotInstalledMessage(PluginCatalogSnapshot snapshot, PluginComparisonSelection selection)
    {
        if (selection.PluginVersion is null)
        {
            return $"Plugin '{selection.PluginId}' is not installed.";
        }

        IReadOnlyList<string> installedVersions = snapshot.InstalledVersions(selection.PluginId);

        return installedVersions.Count == 0
            ? $"Plugin '{selection.PluginId}' version {selection.PluginVersion} is not installed."
            : $"Plugin '{selection.PluginId}' version {selection.PluginVersion} is not installed. Installed version(s): {string.Join(", ", installedVersions)}.";
    }

    private ServiceProvider BuildProvider(LoadedPlugin loaded)
    {
        IServiceCollection services = new ServiceCollection();
        foreach (ServiceDescriptor descriptor in loaded.Registrations.Services)
        {
            services.Add(descriptor);
        }

        // Host services the plugin contract exposes (payload serialization, HTTP)
        // are added last so a plugin cannot shadow them with its own registration.
        configureHostServices?.Invoke(services);

        return services.BuildServiceProvider();
    }

    private static IReadOnlyList<IComparisonMiddleware> ResolveSteps(
        LoadedPlugin loaded,
        IComparisonDefinition definition,
        PluginComparisonSelection selection,
        IServiceProvider services)
    {
        // A profile that lists no steps gets the comparison's defaults; one that
        // lists steps gets exactly those, plus whatever the comparison declares it
        // cannot run without.
        HashSet<string> enabled = new HashSet<string>(
            selection.EnabledStepIds.Count > 0 ? selection.EnabledStepIds : definition.DefaultStepIds,
            StringComparer.OrdinalIgnoreCase);
        enabled.UnionWith(definition.RequiredStepIds);

        List<IComparisonMiddleware> steps = new List<IComparisonMiddleware>();
        steps.AddRange(loaded.Registrations.MiddlewareInstances);
        steps.AddRange(loaded.Registrations.MiddlewareTypes
            .Select(type => (IComparisonMiddleware)services.GetRequiredService(type)));

        IReadOnlyList<IComparisonMiddleware> selectedSteps = steps
            .Where(step => enabled.Contains(step.StepId))
            .ToArray();

        string[] missingRequiredSteps = definition.RequiredStepIds
            .Where(required => !selectedSteps.Any(step => string.Equals(step.StepId, required, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        return missingRequiredSteps.Length == 0
            ? selectedSteps
            : throw new InvalidOperationException(
                $"Comparison '{definition.ComparisonId}' requires step(s) '{string.Join("', '", missingRequiredSteps)}' which plugin '{selection.PluginId}' does not register.");
    }

    private sealed class PluginRunScope : IAsyncDisposable
    {
        private readonly AsyncServiceScope scope;
        private readonly ServiceProvider provider;

        public PluginRunScope(AsyncServiceScope scope, ServiceProvider provider)
        {
            this.scope = scope;
            this.provider = provider;
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync().ConfigureAwait(false);
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }
}
