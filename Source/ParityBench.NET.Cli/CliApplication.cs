using Microsoft.Extensions.DependencyInjection;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Engine;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.Infrastructure.Reports;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string? workspaceRoot = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        RequestCommandParseResult parseResult = RequestCommandParser.Parse(args);
        if (!parseResult.IsSuccess)
        {
            foreach (string parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError).ConfigureAwait(false);
            }

            await error.WriteLineAsync(RequestCommandParser.Usage).ConfigureAwait(false);
            return 2;
        }

        ServiceCollection services = new ServiceCollection();
        RegisterServices(services, workspaceRoot ?? GetDefaultWorkspaceRoot());
        configureServices?.Invoke(services);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();
        RequestCommandRunner runner = serviceProvider.GetRequiredService<RequestCommandRunner>();
        return await runner
            .RunAsync(parseResult.Options!, output, error, cancellationToken)
            .ConfigureAwait(false);
    }

    public static void RegisterServices(IServiceCollection services, string workspaceRoot)
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
        InMemoryRequestComparisonEndpointRegistry endpointDefaults = new InMemoryRequestComparisonEndpointRegistry();
        InMemoryRequestComparisonPresetRegistry presetDefaults = new InMemoryRequestComparisonPresetRegistry();
        RequestComparisonFixtureDefaults.Register(endpointDefaults, presetDefaults);
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
                serviceProvider.GetRequiredService<IContractProfileRegistry>());
        });
        services.AddSingleton<IComparisonRunUseCases, ComparisonRunService>();
        services.AddSingleton<IComparisonRunResultUseCases, ComparisonRunResultService>();
        services.AddSingleton<IReportAssetLocator, ReportAssetLocator>();
        services.AddSingleton<IStaticReportBundleWriter, StaticReportBundleWriter>();
        services.AddSingleton<IRequestComparisonWorkflowUseCases, RequestComparisonWorkflowService>();
        services.AddSingleton<IRequestComparisonDefaultsUseCases, RequestComparisonDefaultsService>();
        services.AddSingleton<RequestCommandRunner>();
    }

    private static string GetDefaultWorkspaceRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParityBench.NET",
            "Workspace");
}