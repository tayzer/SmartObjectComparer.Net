using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;

namespace ComparisonTool.Cli.Infrastructure;

/// <summary>
/// Builds a DI service provider for CLI command execution.
/// </summary>
public static class ServiceProviderFactory
{
    /// <summary>
    /// Creates a fully configured service provider with comparison services registered.
    /// </summary>
    public static ServiceProvider CreateServiceProvider(IConfiguration configuration)
    {
        var services = new ServiceCollection();

        // Logging via Serilog
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger);
        });

        // Core comparison services (XML + JSON) with same model registrations as the web host
        services.AddUnifiedComparisonServices(configuration, options =>
        {
            options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
        });

        // HTTP client for request comparison
        services.AddHttpClient("RequestComparison")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });

        // Request comparison services
        services.AddSingleton<RequestFileParserService>();
        services.AddSingleton<RequestExecutionService>();
        services.AddSingleton<RawTextComparisonService>();
        services.AddSingleton<IComparisonProgressPublisher, ConsoleProgressPublisher>();
        services.AddSingleton<RequestComparisonJobService>();

        // Configuration root
        services.AddSingleton<IConfiguration>(configuration);

        return services.BuildServiceProvider();
    }
}
