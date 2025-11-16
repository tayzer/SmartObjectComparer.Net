// <copyright file="ServiceCollectionExtensions.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

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
/// Extension methods for registering services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add XML comparison services with proper dependency injection.
    /// </summary>
    /// <returns></returns>
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

        services.AddSingleton<IComparisonConfigurationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ComparisonConfigurationService>>();
            var configurationService = new ComparisonConfigurationService(logger);

            // Wire up the cache service
            var cacheService = provider.GetRequiredService<ComparisonResultCacheService>();
            configurationService.SetCacheService(cacheService);

            return configurationService;
        });

        services.AddSingleton<IXmlDeserializationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<XmlDeserializationService>>();
            var serializerFactory = provider.GetRequiredService<XmlSerializerFactory>();
            var configService = provider.GetRequiredService<IComparisonConfigurationService>();

            var service = new XmlDeserializationService(logger, serializerFactory, configService);

            // todo: shouldnt be done here
            service.RegisterDomainModel<ComplexOrderResponse>("ComplexOrderResponse");

            return service;
        });

        services.AddScoped<IComparisonEngine, ComparisonEngine>();
        services.AddScoped<IComparisonOrchestrator, ComparisonOrchestrator>();

        services.AddScoped<IComparisonService, ComparisonService>();

        services.AddScoped<IFileUtilities, FileUtilities>();

        services.AddSingleton<IFileSystemService, FileSystemService>();

        services.AddScoped<DirectoryComparisonService>();

        return services;
    }

    /// <summary>
    /// Add JSON comparison services with proper dependency injection.
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddJsonComparisonServices(this IServiceCollection services)
    {
        // Add JSON deserialization service
        services.AddSingleton<JsonDeserializationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<JsonDeserializationService>>();
            var service = new JsonDeserializationService(logger);

            // Register test domain model for JSON/XML comparison testing
            service.RegisterDomainModel<ComparisonTool.Domain.Models.CustomerOrder>("CustomerOrder");

            return service;
        });

        return services;
    }

    /// <summary>
    /// Add unified comparison services that support both XML and JSON formats.
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection AddUnifiedComparisonServices(this IServiceCollection services, IConfiguration configuration = null)
    {
        // Add both XML and JSON services
        services.AddXmlComparisonServices(configuration);
        services.AddJsonComparisonServices();

        // Add the factory for choosing appropriate services
        services.AddSingleton<DeserializationServiceFactory>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<DeserializationServiceFactory>>();
            return new DeserializationServiceFactory(provider, logger);
        });

        // Add unified deserialization service that can handle both formats
        services.AddSingleton<IDeserializationService>(provider =>
        {
            var factory = provider.GetRequiredService<DeserializationServiceFactory>();
            return factory.GetUnifiedService();
        });

        return services;
    }

    /// <summary>
    /// Register domain models with all deserialization services.
    /// </summary>
    /// <returns></returns>
    public static IServiceCollection RegisterDomainModel<T>(this IServiceCollection services, string modelName)
        where T : class
    {
        services.AddSingleton<Action<IServiceProvider>>(provider => serviceProvider =>
        {
            // Register with unified service if available
            var unifiedService = serviceProvider.GetService<IDeserializationService>();
            if (unifiedService != null)
            {
                unifiedService.RegisterDomainModel<T>(modelName);
                return;
            }

            // Fallback to individual services
            var xmlService = serviceProvider.GetService<IXmlDeserializationService>();
            xmlService?.RegisterDomainModel<T>(modelName);

            var jsonService = serviceProvider.GetService<JsonDeserializationService>();
            jsonService?.RegisterDomainModel<T>(modelName);
        });

        return services;
    }
}
