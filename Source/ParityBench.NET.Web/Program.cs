using Microsoft.AspNetCore.DataProtection;

using MudBlazor.Services;

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
using ParityBench.NET.Engine.Pipeline;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Infrastructure.Reports;
using ParityBench.NET.UI.Results;
using ParityBench.NET.UI.Theming;
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
builder.Services.AddScoped<ParityBenchThemeState>();
string fixtureBaseUrl = builder.Configuration["ParityBench:RequestDefaults:FixtureBaseUrl"]
    ?? RequestComparisonFixtureDefaults.DefaultFixtureBaseUrl;
RegisterV2Services(builder.Services, workspaceRoot, fixtureBaseUrl, builder.Configuration["ParityBench:AcceptedDifferences:StorePath"], builder.Configuration);

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

// Bounded connection lifetime so a long-lived singleton HttpClient still
// picks up DNS changes for endpoints under test instead of pinning forever.
static HttpClient CreateSharedHttpClient() =>
    new HttpClient(new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) });

static void RegisterV2Services(IServiceCollection services, string workspaceRoot, string fixtureBaseUrl, string? acceptedDifferenceStorePath, IConfiguration configuration)
{
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
    services.AddSingleton<IRequestComparisonEndpointRegistry>(endpointDefaults);
    services.AddSingleton<IRequestComparisonPresetRegistry>(presetDefaults);
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
    services.AddScoped<IRequestSourcePicker, NoOpRequestSourcePicker>();
}
