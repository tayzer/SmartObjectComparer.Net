using Microsoft.AspNetCore.DataProtection;

using MudBlazor.Services;

using ParityBench.NET.Application.AlternateContracts;
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

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddConsole();

string workspaceRoot = builder.Configuration["ParityBench:WorkspaceRoot"]
    ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ParityBench.NET",
        "Workspace");
Directory.CreateDirectory(workspaceRoot);

builder.Services
    .AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(workspaceRoot, "data-protection-keys")));

builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddMudServices();
RegisterV2Services(builder.Services, workspaceRoot);

WebApplication app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();

static void RegisterV2Services(IServiceCollection services, string workspaceRoot)
{
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
        return registry;
    });
    services.AddSingleton<IAlternateContractProfileRegistry>(serviceProvider =>
    {
        AlternateContractProfileRegistry registry = new AlternateContractProfileRegistry();
        registry.Register(BuiltInAlternateContractProfiles.CreateSampleSoapToJson(serviceProvider.GetRequiredService<IContractPayloadSerializer>()));
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
            serviceProvider.GetRequiredService<IAlternateContractProfileRegistry>());
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