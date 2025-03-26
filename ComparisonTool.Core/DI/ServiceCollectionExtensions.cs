using System.Configuration;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.DI;

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
            services.Configure<ComparisonConfigurationOptions>(configuration.GetSection("ComparisonSettings"));
        }

        services.AddSingleton<XmlSerializerFactory>();

        // Register the XML deserialization service with the factory dependency
        services.AddSingleton<IXmlDeserializationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<XmlDeserializationService>>();
            var serializerFactory = provider.GetRequiredService<XmlSerializerFactory>();

            var service = new XmlDeserializationService(logger, serializerFactory);

            // todo: shouldnt be done here
            service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");

            return service;
        });

        // Register the comparison configuration service
        services.AddSingleton<IComparisonConfigurationService, ComparisonConfigurationService>();

        // Register the comparison service
        services.AddScoped<IComparisonService, ComparisonService>();

        // Register utilities
        services.AddScoped<IFileUtilities, FileUtilities>();

        return services;
    }
}