using System.IO;
using System.Net.Http;
using System.Windows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Engine;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Infrastructure.Reports;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Workflow;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? host;

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

                RegisterV2Services(services, workspaceRoot);
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);

        MainWindow mainWindow = new MainWindow(host.Services);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }

        base.OnExit(e);
    }

    private static void RegisterV2Services(IServiceCollection services, string workspaceRoot)
    {
        Directory.CreateDirectory(workspaceRoot);
        services.AddSingleton(new HttpClient());
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
        services.AddSingleton<IResponseModelRegistry>(_ =>
        {
            ResponseModelRegistry registry = new ResponseModelRegistry();
            BuiltInResponseModelRegistration.Register(registry);
            ConsumerReportFixtureResponseModelRegistration.Register(registry);
            return registry;
        });
        services.AddSingleton<IContractProfileRegistry>(serviceProvider =>
        {
            ContractProfileRegistry registry = new ContractProfileRegistry();
            registry.Register(BuiltInContractProfiles.CreateSampleSoapToJson(serviceProvider.GetRequiredService<IContractPayloadSerializer>()));
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
                serviceProvider.GetRequiredService<IContractProfileRegistry>());
        });
        services.AddSingleton<IComparisonRunUseCases, ComparisonRunService>();
        services.AddSingleton<IComparisonRunResultUseCases, ComparisonRunResultService>();
        services.AddSingleton<IReportAssetLocator, ReportAssetLocator>();
        services.AddSingleton<IStaticReportBundleWriter, StaticReportBundleWriter>();
        services.AddSingleton<IRequestComparisonWorkflowUseCases, RequestComparisonWorkflowService>();
        services.AddSingleton<IComparisonRunJobUseCases, ComparisonRunJobService>();
        services.AddScoped<IRunResultsViewDataSource, ApplicationRunResultsViewDataSource>();
        services.AddScoped<IRunWorkflowViewDataSource, ApplicationRunWorkflowViewDataSource>();
    }
}