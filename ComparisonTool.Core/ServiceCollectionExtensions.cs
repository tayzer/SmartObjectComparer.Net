using System.Configuration;
using ComparisonTool.Core.V2;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core;

/// <summary>
/// Extension methods for registering services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add XML comparison services with proper dependency injection
    /// </summary>
    public static IServiceCollection AddXmlComparisonServices(this IServiceCollection services, IConfiguration configuration = null)
    {
        // Add configuration if provided
        if (configuration != null)
        {
            services.AddOptions();
            services.Configure<ComparisonConfigOptions>(configuration.GetSection("ComparisonSettings"));
        }

        // Register the XML deserialization service
        services.AddSingleton<IXmlDeserializationService>(provider => {
            var logger = provider.GetRequiredService<ILogger<XmlDeserializationService>>();
            var service = new XmlDeserializationService(logger);

            // Register known domain models
            service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");

            return service;
        });

        // Register the comparison configuration service
        services.AddSingleton<IComparisonConfigurationService, ComparisonConfigurationService>();

        // Register the comparison service
        services.AddScoped<IComparisonService, ComparisonService>();

        // Register utilities
        services.AddScoped<IFileUtils, FileUtils>();

        // Register the legacy service for backward compatibility during transition
        services.AddSingleton<XmlComparisonService>();

        return services;
    }
}