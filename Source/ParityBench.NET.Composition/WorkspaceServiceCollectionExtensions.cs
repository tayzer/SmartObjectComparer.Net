using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
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
using ParityBench.NET.Infrastructure.Reports;
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
                serviceProvider.GetRequiredService<IContractProfileRegistry>(),
                serviceProvider.GetRequiredService<IObservabilityRecorder>(),
                serviceProvider.GetRequiredService<IRunCleanupStage>());
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
