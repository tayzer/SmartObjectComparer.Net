using Microsoft.Extensions.DependencyInjection;

namespace ComparisonTool.Core;

/// <summary>
/// Extension methods for registering services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add XML comparison services
    /// </summary>
    public static IServiceCollection AddXmlComparisonServices(this IServiceCollection services)
    {
        // Register the comparison service as a singleton
        services.AddSingleton<XmlComparisonService>(provider =>
        {
            var service = new XmlComparisonService();

            // Register domain models
            service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");

            // Configure default settings
            service.SetIgnoreCollectionOrder(false);

            return service;
        });

        return services;
    }
}