using Mapster;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupExampleServiceCollectionExtensions
{
    public const string ConfigurationSectionName = "ClientCustomerLookup:Tokens";
    public const string ComparisonConfigurationSectionName = "ClientCustomerLookup:Comparison";

    public static IServiceCollection AddClientCustomerLookupExample(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<ClientCustomerLookupTokenOptions>(
            configuration.GetSection(ConfigurationSectionName));
        services.Configure<ComparisonRuleDefaultsFileOptions>(options =>
        {
            configuration.GetSection(ComparisonConfigurationSectionName).Bind(options);
            options.IgnoreXmlNamespacesOverride = true;
        });
        services.AddSingleton(_ => ClientCustomerLookupMapsterConfig.CreateConfig());
        services.AddHttpClient<IClientCustomerLookupTokenProvider, ClientCustomerLookupTokenProvider>();
        return services;
    }

    public static void RegisterClientCustomerLookupResponseModel(
        this IResponseModelRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        registry.Register<ClientCustomerLookupResponse>(ClientCustomerLookupProfileFactory.ResponseModelName);
    }

    public static void RegisterClientCustomerLookupProfile(
        this ContractProfileRegistry registry,
        IServiceProvider serviceProvider)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(serviceProvider);

        registry.Register(ClientCustomerLookupProfileFactory.Create(
            serviceProvider.GetRequiredService<IContractPayloadSerializer>(),
            serviceProvider.GetRequiredService<IClientCustomerLookupTokenProvider>(),
            serviceProvider.GetRequiredService<TypeAdapterConfig>(),
            ComparisonRuleDefaultsFileLoader.Load(
                serviceProvider.GetRequiredService<IOptions<ComparisonRuleDefaultsFileOptions>>().Value)));
    }
}

