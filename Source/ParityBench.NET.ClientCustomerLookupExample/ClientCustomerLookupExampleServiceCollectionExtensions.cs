using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupExampleServiceCollectionExtensions
{
    public const string ConfigurationSectionName = "ClientCustomerLookup:Tokens";
    public const string ComparisonConfigurationSectionName = "ClientCustomerLookup:Comparison";

    public static IServiceCollection AddClientCustomerLookupExample(
        this IServiceCollection services,
        IConfiguration configuration,
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry,
        string manualRunRoot)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(endpointRegistry);
        ArgumentNullException.ThrowIfNull(presetRegistry);

        services.Configure<ClientCustomerLookupTokenOptions>(
            configuration.GetSection(ConfigurationSectionName));
        services.Configure<ComparisonRuleDefaultsFileOptions>(options =>
        {
            configuration.GetSection(ComparisonConfigurationSectionName).Bind(options);
            options.IgnoreXmlNamespacesOverride = true;
        });
        services.AddSingleton(_ => ClientCustomerLookupMapsterConfig.CreateConfig());
        services.AddHttpClient<IClientCustomerLookupTokenProvider, ClientCustomerLookupTokenProvider>();

        ClientCustomerLookupExampleDefaults.Register(endpointRegistry, presetRegistry, configuration, manualRunRoot);
        ClientCustomerLookupExampleDefaults.RegisterVolumePreset(presetRegistry, configuration, manualRunRoot);

        services.AddSingleton<IResponseModelContributor, ClientCustomerLookupResponseModelContributor>();
        services.AddSingleton<IContractProfileContributor, ClientCustomerLookupProfileContributor>();
        return services;
    }

    private static void RegisterClientCustomerLookupResponseModel(
        this IResponseModelRegistry registry)
    {
        registry.Register<ClientCustomerLookupResponse>(ClientCustomerLookupProfileFactory.ResponseModelName);
    }

    private static void RegisterClientCustomerLookupProfile(
        this IContractProfileRegistry registry,
        IServiceProvider serviceProvider)
    {
        registry.Register(ClientCustomerLookupProfileFactory.Create(
            serviceProvider.GetRequiredService<IContractPayloadSerializer>(),
            serviceProvider.GetRequiredService<IClientCustomerLookupTokenProvider>(),
            serviceProvider.GetRequiredService<TypeAdapterConfig>(),
            ComparisonRuleDefaultsFileLoader.Load(
                serviceProvider.GetRequiredService<IOptions<ComparisonRuleDefaultsFileOptions>>().Value)));
    }

    private sealed class ClientCustomerLookupResponseModelContributor : IResponseModelContributor
    {
        public void Register(IResponseModelRegistry registry) => registry.RegisterClientCustomerLookupResponseModel();
    }

    private sealed class ClientCustomerLookupProfileContributor : IContractProfileContributor
    {
        public void Register(IContractProfileRegistry registry, IServiceProvider serviceProvider) =>
            registry.RegisterClientCustomerLookupProfile(serviceProvider);
    }
}

