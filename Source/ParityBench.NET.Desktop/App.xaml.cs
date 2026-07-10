using System.IO;
using System.Net.Http;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;
using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Desktop.Services;
using ParityBench.NET.Engine;
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Infrastructure.Reports;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Workflow;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? host;

    // Bounded connection lifetime so a long-lived singleton HttpClient still
    // picks up DNS changes for endpoints under test instead of pinning forever.
    private static HttpClient CreateSharedHttpClient() =>
        new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices((context, services) =>
            {
                services.AddWpfBlazorWebView();
                services.AddMudServices();

                string workspaceRoot = context.Configuration["ParityBench:WorkspaceRoot"]
                    ?? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ParityBench.NET",
                        "Workspace");

                string fixtureBaseUrl = context.Configuration["ParityBench:RequestDefaults:FixtureBaseUrl"]
                    ?? RequestComparisonFixtureDefaults.DefaultFixtureBaseUrl;

                services.AddClientCustomerLookupExample(context.Configuration);
                RegisterV2Services(services, workspaceRoot, fixtureBaseUrl, context.Configuration["ParityBench:AcceptedDifferences:StorePath"], context.Configuration);
            })
            .Build();

        await host.StartAsync();

        MainWindow mainWindow = new MainWindow(host.Services);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync();
            host.Dispose();
        }

        base.OnExit(e);
    }

    private static string FindManualRunRoot()
    {
        foreach (string candidateRoot in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new DirectoryInfo(candidateRoot);
            while (directory is not null)
            {
                string manualRunRoot = Path.Combine(directory.FullName, "Examples", "ParityBench.NET.ManualRuns");
                if (Directory.Exists(manualRunRoot))
                {
                    return manualRunRoot;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine("Examples", "ParityBench.NET.ManualRuns");
    }
    private static void RegisterV2Services(IServiceCollection services, string workspaceRoot, string fixtureBaseUrl, string? acceptedDifferenceStorePath, IConfiguration configuration)
    {
        Directory.CreateDirectory(workspaceRoot);
        services.AddParityBenchObservability(configuration);
        services.AddRetentionConfiguration(configuration);
        services.Configure<RequestComparisonRunDefaults>(configuration.GetSection("RequestComparison:Defaults"));
        services.AddSingleton(CreateSharedHttpClient());
        services.AddSingleton<IRequestBatchStore>(_ => new FileSystemRequestBatchStore(workspaceRoot));
        services.AddSingleton<IRunStore>(_ => new FileSystemRunStore(workspaceRoot));
        services.AddSingleton<IRunDetailStore>(_ => new FileSystemRunDetailStore(workspaceRoot));
        services.AddSingleton<IRunArtifactStore>(_ => new FileSystemRunArtifactStore(workspaceRoot));
        services.AddSingleton<IAcceptedDifferenceUseCases>(_ => new FileSystemAcceptedDifferenceStore(workspaceRoot, acceptedDifferenceStorePath));
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
        ClientCustomerLookupExampleDefaults.Register(endpointDefaults, presetDefaults, configuration, FindManualRunRoot());
        services.AddSingleton<IRequestComparisonEndpointRegistry>(endpointDefaults);
        services.AddSingleton<IRequestComparisonPresetRegistry>(presetDefaults);
        services.AddSingleton<IResponseModelRegistry>(_ =>
        {
            ResponseModelRegistry registry = new ResponseModelRegistry();
            BuiltInResponseModelRegistration.Register(registry);
            ConsumerReportFixtureResponseModelRegistration.Register(registry);
            registry.RegisterClientCustomerLookupResponseModel();
            return registry;
        });
        services.AddSingleton<IContractProfileRegistry>(serviceProvider =>
        {
            ContractProfileRegistry registry = new ContractProfileRegistry();
            registry.Register(BuiltInContractProfiles.CreateSampleSoapToJson(serviceProvider.GetRequiredService<IContractPayloadSerializer>()));
            registry.RegisterClientCustomerLookupProfile(serviceProvider);
            return registry;
        });
        services.AddSingleton<IComparisonRunExecutor>(serviceProvider =>
        {
            IRunArtifactStore artifactStore = serviceProvider.GetRequiredService<IRunArtifactStore>();
            IResponseComparer comparer = new SelectableResponseComparer(
                new HashOnlyResponseComparer(),
                new CompareNetObjectsResponseComparer(artifactStore, serviceProvider.GetRequiredService<IResponseBodyDeserializer>()),
                serviceProvider.GetRequiredService<IResponseModelRegistry>());

            return new BasicComparisonRunExecutor(
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
        services.AddSingleton<IComparisonRunJobUseCases, ComparisonRunJobService>();
        services.AddSingleton<IRequestComparisonDefaultsUseCases, RequestComparisonDefaultsService>();
        services.AddScoped<IRunResultsViewDataSource, ApplicationRunResultsViewDataSource>();
        services.AddScoped<IRunWorkflowViewDataSource, ApplicationRunWorkflowViewDataSource>();
        services.AddScoped<IRequestSourcePicker, DesktopRequestSourcePicker>();
    }
}
