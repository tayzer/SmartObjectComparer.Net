using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Secrets;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Plugins;
using ParityBench.NET.Infrastructure.Reports;
using ParityBench.NET.Infrastructure.Secrets;
using ParityBench.NET.Infrastructure.Worker;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Workflow;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Composition;

/// <summary>
/// Composition-root helpers shared by the Cli, Web and Desktop hosts so the
/// request-comparison workspace wiring only has to be maintained in one place.
/// </summary>
public static class WorkspaceServiceCollectionExtensions
{
    // Bounded connection lifetime so a long-lived singleton HttpClient still
    // picks up DNS changes for endpoints under test instead of pinning forever.
    private static HttpClient CreateSharedHttpClient() =>
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    /// <summary>
    /// Registers the request-comparison workspace services common to every host
    /// (Cli, Web, Desktop): stores, run execution, comparison registries and use cases.
    /// </summary>
    /// <param name="configureObservability">Per-host overrides layered on top of configuration-bound observability options.</param>
    /// <param name="configureRequestComparisonFixtures">
    /// Optional hook invoked after the built-in fixture defaults are registered but before the
    /// endpoint/preset registries are captured as singletons, so a host can contribute additional
    /// endpoints/presets (e.g. Cli/Desktop's ClientCustomerLookupExample) into the same registries.
    /// </param>
    public static IServiceCollection AddParityBenchWorkspaceServices(
        this IServiceCollection services,
        IConfiguration configuration,
        string workspaceRoot,
        string fixtureBaseUrl,
        Action<ObservabilityOptions>? configureObservability = null,
        Action<IServiceCollection, InMemoryRequestComparisonEndpointRegistry, InMemoryRequestComparisonPresetRegistry>? configureRequestComparisonFixtures = null)
    {
        Directory.CreateDirectory(workspaceRoot);

        services.AddParityBenchObservability(configuration, configureObservability);
        services.AddRetentionConfiguration(configuration);
        services.Configure<RequestComparisonRunDefaults>(configuration.GetSection("RequestComparison:Defaults"));

        services.AddSingleton(CreateSharedHttpClient());
        services.AddSingleton<IRequestBatchStore>(_ => new FileSystemRequestBatchStore(workspaceRoot));
        services.AddSingleton<IRunStore>(_ => new FileSystemRunStore(workspaceRoot));
        services.AddSingleton<IRunDetailStore>(_ => new FileSystemRunDetailStore(workspaceRoot));
        services.AddSingleton<IRunArtifactStore>(_ => new FileSystemRunArtifactStore(workspaceRoot));
        services.AddSingleton<IEndpointRequestSender, HttpClientEndpointRequestSender>();
        services.AddSingleton<IRunCancellationRegistry, InMemoryRunCancellationRegistry>();
        services.AddSingleton<IRunIdGenerator, GuidRunIdGenerator>();
        services.AddSingleton<IRequestBatchReferenceGenerator, GuidRequestBatchReferenceGenerator>();
        services.AddSingleton<IRunEventPublisher, NoOpRunEventPublisher>();
        services.AddSingleton<IResponseBodyDeserializer, JsonXmlResponseBodyDeserializer>();
        services.AddSingleton<IContractPayloadSerializer, JsonXmlContractPayloadSerializer>();
        services.AddSingleton<RetentionPolicyEvaluator>();
        services.AddSingleton<IRunCleanupStage, RetentionCleanupStage>();

        InMemoryRequestComparisonEndpointRegistry endpointDefaults = new InMemoryRequestComparisonEndpointRegistry();
        InMemoryRequestComparisonPresetRegistry presetDefaults = new InMemoryRequestComparisonPresetRegistry();
        RequestComparisonFixtureDefaults.Register(endpointDefaults, presetDefaults, fixtureBaseUrl);
        configureRequestComparisonFixtures?.Invoke(services, endpointDefaults, presetDefaults);
        services.AddSingleton<IRequestComparisonEndpointRegistry>(endpointDefaults);
        services.AddSingleton<IRequestComparisonPresetRegistry>(presetDefaults);

        services.AddSingleton<IResponseModelRegistry>(serviceProvider =>
        {
            ResponseModelRegistry registry = new ResponseModelRegistry();
            BuiltInResponseModelRegistration.Register(registry);
            ConsumerReportFixtureResponseModelRegistration.Register(registry);
            foreach (IResponseModelContributor contributor in serviceProvider.GetServices<IResponseModelContributor>())
            {
                contributor.Register(registry);
            }

            return registry;
        });
        services.AddSingleton<IContractProfileRegistry>(serviceProvider =>
        {
            ContractProfileRegistry registry = new ContractProfileRegistry();
            registry.Register(BuiltInContractProfiles.CreateSampleSoapToJson(serviceProvider.GetRequiredService<IContractPayloadSerializer>()));
            foreach (IContractProfileContributor contributor in serviceProvider.GetServices<IContractProfileContributor>())
            {
                contributor.Register(registry, serviceProvider);
            }

            return registry;
        });
        services.AddSingleton<IRunProfileStore>(_ => new FileSystemRunProfileStore(workspaceRoot));
        // Order is the policy: an environment variable beats the persisted store, so
        // CI and tests can run any profile without touching an operator's machine.
        services.AddSingleton<ISecretStore>(_ => new ChainedSecretStore(
            new EnvironmentVariableSecretStore(),
            new DpapiSecretStore(workspaceRoot)));
        services.AddSingleton<SecretResolver>();
        services.AddSingleton<RunProfileResolver>();

        services.AddSingleton(_ => new PluginCatalog(ResolvePluginDirectories(configuration, workspaceRoot)));
        services.AddSingleton<PluginLoader>();
        services.AddSingleton<IPluginMetadataProvider, PluginMetadataProvider>();
        services.AddSingleton<IComparisonPlanFactory>(serviceProvider => new PluginComparisonPlanFactory(
            serviceProvider.GetRequiredService<PluginCatalog>(),
            serviceProvider.GetRequiredService<PluginLoader>(),
            // The host services a plugin's steps are allowed to resolve. Anything not
            // registered here is simply not reachable from plugin code.
            pluginServices =>
            {
                pluginServices.AddSingleton(serviceProvider.GetRequiredService<IContractPayloadSerializer>());
                pluginServices.AddSingleton(serviceProvider.GetRequiredService<HttpClient>());
            }));

        services.AddSingleton<IComparisonRunExecutor>(serviceProvider =>
        {
            IRunArtifactStore artifactStore = serviceProvider.GetRequiredService<IRunArtifactStore>();
            IResponseComparer comparer = new SelectableResponseComparer(
                new HashOnlyResponseComparer(),
                new CompareNetObjectsResponseComparer(artifactStore, serviceProvider.GetRequiredService<IResponseBodyDeserializer>()),
                serviceProvider.GetRequiredService<IResponseModelRegistry>());

            return new ComparisonRunExecutor(
                serviceProvider.GetRequiredService<IRequestBatchStore>(),
                serviceProvider.GetRequiredService<IEndpointRequestSender>(),
                artifactStore,
                serviceProvider.GetRequiredService<IRunDetailStore>(),
                comparer,
                serviceProvider.GetRequiredService<IComparisonPlanFactory>(),
                serviceProvider.GetRequiredService<IObservabilityRecorder>(),
                serviceProvider.GetRequiredService<IRunCleanupStage>(),
                serviceProvider.GetRequiredService<IContractPayloadSerializer>());
        });

        services.AddSingleton<IComparisonRunUseCases, ComparisonRunService>();
        services.AddSingleton<IComparisonRunResultUseCases, ComparisonRunResultService>();
        services.AddSingleton<IReportAssetLocator, ReportAssetLocator>();
        services.AddSingleton<IStaticReportBundleWriter, StaticReportBundleWriter>();
        services.AddSingleton<IRequestComparisonWorkflowUseCases, RequestComparisonWorkflowService>();
        services.AddSingleton<IRequestComparisonDefaultsUseCases, RequestComparisonDefaultsService>();

        return services;
    }

    /// <summary>
    /// Swaps in-process run execution for out-of-process execution in a worker
    /// process, so client plugin code never runs in the host. Opt-in per host:
    /// call this after <see cref="AddParityBenchWorkspaceServices"/>. The worker
    /// process itself must not call it, or it would try to spawn a worker of its own.
    /// </summary>
    public static IServiceCollection UseWorkerProcessExecution(
        this IServiceCollection services,
        IConfiguration configuration,
        string workspaceRoot,
        string fixtureBaseUrl,
        string? workerExecutablePath = null)
    {
        services.Configure<WorkerExecutionOptions>(options =>
        {
            options.WorkspaceRoot = workspaceRoot;
            options.FixtureBaseUrl = fixtureBaseUrl;
            options.WorkerExecutablePath = workerExecutablePath ?? configuration["Worker:ExecutablePath"];
        });

        // Replace the in-process executor registered by AddParityBenchWorkspaceServices.
        services.RemoveAll<IComparisonRunExecutor>();
        services.AddSingleton<IComparisonRunExecutor, WorkerComparisonRunExecutor>();
        return services;
    }

    /// <summary>
    /// Resolves where installed plugin packages are looked for: the configured
    /// directories if any, otherwise the app's own <c>plugins</c> folder plus the
    /// workspace's, so a client can drop a package next to their data without
    /// touching the installation.
    /// </summary>
    private static IReadOnlyList<string> ResolvePluginDirectories(IConfiguration configuration, string workspaceRoot)
    {
        string[] configured = configuration
            .GetSection("Plugins:Directories")
            .Get<string[]>() ?? Array.Empty<string>();

        return configured.Length > 0
            ? configured
            : new[]
            {
                Path.Combine(AppContext.BaseDirectory, "plugins"),
                Path.Combine(workspaceRoot, "plugins"),
            };
    }

    /// <summary>
    /// Registers the additional services needed by the Blazor-based hosts (Web, Desktop)
    /// on top of <see cref="AddParityBenchWorkspaceServices"/>. Not used by Cli, which has
    /// no accepted-differences UI or job/view-data-source concept.
    /// </summary>
    public static IServiceCollection AddParityBenchUiServices(
        this IServiceCollection services,
        string workspaceRoot,
        string? acceptedDifferenceStorePath)
    {
        services.AddSingleton<IAcceptedDifferenceUseCases>(_ => new FileSystemAcceptedDifferenceStore(workspaceRoot, acceptedDifferenceStorePath));
        services.AddSingleton<IComparisonRunJobUseCases, ComparisonRunJobService>();
        services.AddScoped<IRunResultsViewDataSource, ApplicationRunResultsViewDataSource>();
        services.AddScoped<IRunWorkflowViewDataSource, ApplicationRunWorkflowViewDataSource>();

        return services;
    }
}
