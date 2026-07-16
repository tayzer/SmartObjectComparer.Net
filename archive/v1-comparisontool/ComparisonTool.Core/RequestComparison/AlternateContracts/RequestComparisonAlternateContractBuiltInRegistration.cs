using ComparisonTool.Core.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

public static class RequestComparisonAlternateContractBuiltInRegistration
{
    public static void RegisterXmlComparisonModels(XmlComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        RequestComparisonAlternateContractSampleRegistration.RegisterComparisonModels(options);
    }

    public static void RegisterSharedComparisonModels(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RequestComparisonExpectedJsonCustomerLookupRegistration.RegisterSharedComparisonModels(services);
    }

    public static IServiceCollection AddBuiltInRequestComparisonAlternateContracts(
        this IServiceCollection services,
        IConfiguration? configuration = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSupportServices(configuration);

        return services.AddRequestComparisonAlternateContractProfiles(options =>
        {
            RequestComparisonAlternateContractSampleRegistration.RegisterProfiles(options);
            RequestComparisonExpectedJsonCustomerLookupRegistration.RegisterProfiles(options);
        });
    }
}