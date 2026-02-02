// <copyright file="ServiceCollectionExtensions.cs" company="PlaceholderCompany">



using System.Xml.Serialization;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using ComparisonTool.Web.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using CoreXmlSerializerFactory = ComparisonTool.Core.Serialization.XmlSerializerFactory;

namespace ComparisonTool.Core.DI;

/// <summary>
/// Extension methods for registering services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add XML comparison services with proper dependency injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional configuration for comparison settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXmlComparisonServices(this IServiceCollection services, IConfiguration? configuration = null)
        => services.AddXmlComparisonServices(configuration, configureOptions: null);

    /// <summary>
    /// Add XML comparison services with custom domain model registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure domain models and serializers.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddXmlComparisonServices(options => {
    ///     // Register a domain model (uses default serializer with namespace handling)
    ///     options.RegisterDomainModel&lt;MyCustomOrder&gt;("MyCustomOrder");
    ///
    ///     // Register with custom serializer for special XML structure
    ///     options.RegisterDomainModelWithSerializer&lt;LegacyOrder&gt;("LegacyOrder",
    ///         () => new XmlSerializer(typeof(LegacyOrder), new XmlRootAttribute("Order")));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddXmlComparisonServices(this IServiceCollection services, Action<XmlComparisonOptions> configureOptions)
        => services.AddXmlComparisonServices(configuration: null, configureOptions);

    /// <summary>
    /// Add XML comparison services with configuration and custom domain model registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional configuration for comparison settings.</param>
    /// <param name="configureOptions">Action to configure domain models and serializers.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddXmlComparisonServices(
        this IServiceCollection services,
        IConfiguration? configuration,
        Action<XmlComparisonOptions>? configureOptions)
    {
        // Build options from configuration action
        var options = CreateAndConfigureOptions(configureOptions);

        ConfigureOptionsFromConfiguration(services, configuration);
        RegisterCoreServices(services, options);
        RegisterComparisonServices(services);

        return services;
    }

    /// <summary>
    /// Add JSON comparison services with proper dependency injection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional configuration for comparison settings.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddUnifiedComparisonServices(this IServiceCollection services, IConfiguration? configuration = null)
        => services.AddUnifiedComparisonServices(configuration, configureOptions: null);

    /// <summary>
    /// Add unified comparison services with custom domain model registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure domain models and serializers.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // In Program.cs - register your own domain models
    /// builder.Services.AddUnifiedComparisonServices(options => {
    ///     // Register your custom domain model
    ///     options.RegisterDomainModel&lt;MyCustomOrder&gt;("MyCustomOrder");
    ///
    ///     // Register with custom serializer for special XML structure
    ///     options.RegisterDomainModelWithSerializer&lt;LegacyOrder&gt;("LegacyOrder",
    ///         () => new XmlSerializer(typeof(LegacyOrder), new XmlRootAttribute("Order")));
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddUnifiedComparisonServices(this IServiceCollection services, Action<XmlComparisonOptions> configureOptions)
        => services.AddUnifiedComparisonServices(configuration: null, configureOptions);

    /// <summary>
    /// Add unified comparison services with configuration and custom domain model registration.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Optional configuration for comparison settings.</param>
    /// <param name="configureOptions">Action to configure domain models and serializers.</param>
    /// <returns>The service collection for chaining.</returns>
    /// <example>
    /// <code>
    /// // In Program.cs - register your own domain models alongside the built-in ones
    /// builder.Services.AddUnifiedComparisonServices(builder.Configuration, options => {
    ///     // The built-in ComplexOrderResponse is automatically registered
    ///     // Add your own models here:
    ///     options.RegisterDomainModel&lt;MyCustomOrder&gt;("MyCustomOrder");
    ///     options.RegisterDomainModel&lt;SoapEnvelope&gt;("SoapEnvelope");
    /// });
    /// </code>
    /// </example>
    public static IServiceCollection AddUnifiedComparisonServices(
        this IServiceCollection services,
        IConfiguration? configuration,
        Action<XmlComparisonOptions>? configureOptions)
    {
        // Add both XML and JSON services, passing through the options
        services.AddXmlComparisonServices(configuration, configureOptions);
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
    /// <typeparam name="T">The domain model type.</typeparam>
    /// <param name="services">The service collection.</param>
    /// <param name="modelName">The display name for this model.</param>
    /// <returns>The service collection for chaining.</returns>
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

    private static XmlComparisonOptions CreateAndConfigureOptions(Action<XmlComparisonOptions>? configureOptions)
    {
        var options = new XmlComparisonOptions();

        // Always register the built-in ComplexOrderResponse model
        options.RegisterSerializer<ComplexOrderResponse>(() =>
        {
            var serializer = new XmlSerializer(
                typeof(ComplexOrderResponse),
                new XmlRootAttribute
                {
                    ElementName = "OrderManagementResponse",
                    Namespace = string.Empty,
                });
            return serializer;
        });
        options.RegisterDomainModel<ComplexOrderResponse>("ComplexOrderResponse");

        // Apply user's custom registrations
        configureOptions?.Invoke(options);

        return options;
    }

    private static void ConfigureOptionsFromConfiguration(IServiceCollection services, IConfiguration? configuration)
    {
        if (configuration != null)
        {
            services.AddOptions();
            services.Configure<ComparisonConfigurationOptions>(configuration.GetSection("ComparisonSettings"));
        }
    }

    private static void RegisterCoreServices(IServiceCollection services, XmlComparisonOptions options)
    {
        services.AddSingleton<CoreXmlSerializerFactory>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<CoreXmlSerializerFactory>>();
            var factory = new CoreXmlSerializerFactory(logger);
            options.ApplySerializerRegistrations(factory);
            return factory;
        });

        services.AddSingleton<PerformanceTracker>();
        services.AddSingleton<SystemResourceMonitor>();
        services.AddSingleton<ComparisonResultCacheService>();

        services.AddSingleton<IComparisonConfigurationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<ComparisonConfigurationService>>();
            var configurationService = new ComparisonConfigurationService(logger);
            var cacheService = provider.GetRequiredService<ComparisonResultCacheService>();
            configurationService.SetCacheService(cacheService);
            return configurationService;
        });

        services.AddSingleton<IXmlDeserializationService>(provider =>
        {
            var logger = provider.GetRequiredService<ILogger<XmlDeserializationService>>();
            var serializerFactory = provider.GetRequiredService<CoreXmlSerializerFactory>();
            var configService = provider.GetRequiredService<IComparisonConfigurationService>();
            var service = new XmlDeserializationService(logger, serializerFactory, configService);
            options.ApplyDomainModelRegistrations(service);
            return service;
        });
    }

    private static void RegisterComparisonServices(IServiceCollection services)
    {
        services.AddScoped<IComparisonEngine, ComparisonEngine>();
        services.AddScoped<IComparisonOrchestrator>(CreateComparisonOrchestrator);
        services.AddScoped<HighPerformanceComparisonPipeline>();
        services.AddScoped<IComparisonService, ComparisonService>();
        services.AddScoped<IFileUtilities, FileUtilities>();
        services.AddSingleton<IFileSystemService, FileSystemService>();
        services.AddSingleton<IComparisonLogService, ComparisonLogService>();
        services.AddScoped<DirectoryComparisonService>();
    }

    private static ComparisonOrchestrator CreateComparisonOrchestrator(IServiceProvider provider) => new ComparisonOrchestrator(
        provider.GetRequiredService<ILogger<ComparisonOrchestrator>>(),
        provider.GetRequiredService<IXmlDeserializationService>(),
        provider.GetRequiredService<IComparisonConfigurationService>(),
        provider.GetRequiredService<IFileSystemService>(),
        provider.GetRequiredService<PerformanceTracker>(),
        provider.GetRequiredService<SystemResourceMonitor>(),
        provider.GetRequiredService<ComparisonResultCacheService>(),
        provider.GetRequiredService<IComparisonEngine>(),
        provider.GetService<DeserializationServiceFactory>(),
        provider.GetRequiredService<ILoggerFactory>());
}
