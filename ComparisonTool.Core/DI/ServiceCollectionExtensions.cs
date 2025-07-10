using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using ComparisonTool.Web.Services;
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
        if (configuration != null)
        {
            services.AddOptions();
            services.Configure<ComparisonConfigurationOptions>(configuration.GetSection("ComparisonSettings"));
        }

        services.AddSingleton<XmlSerializerFactory>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<XmlSerializerFactory>>();
            return new XmlSerializerFactory(logger);
        });

        // Add performance tracking service
        services.AddSingleton<PerformanceTracker>();
        
        // Add system resource monitor
        services.AddSingleton<SystemResourceMonitor>();

        // Add comparison result cache service
        services.AddSingleton<ComparisonResultCacheService>();

        services.AddSingleton<IXmlDeserializationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<XmlDeserializationService>>();
            var serializerFactory = provider.GetRequiredService<XmlSerializerFactory>();

            var service = new XmlDeserializationService(logger, serializerFactory);

            // todo: shouldnt be done here
            service.RegisterDomainModel<ComplexOrderResponse>("ComplexOrderResponse");

            return service;
        });

        services.AddSingleton<IComparisonConfigurationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ComparisonConfigurationService>>();
            var configurationService = new ComparisonConfigurationService(logger);
            
            // Wire up the cache service
            var cacheService = provider.GetRequiredService<ComparisonResultCacheService>();
            configurationService.SetCacheService(cacheService);
            
            return configurationService;
        });

        services.AddScoped<IComparisonService, ComparisonService>();

        services.AddScoped<IFileUtilities, FileUtilities>();

        services.AddSingleton<IFileSystemService, FileSystemService>();

        services.AddScoped<DirectoryComparisonService>();

        return services;
    }
}