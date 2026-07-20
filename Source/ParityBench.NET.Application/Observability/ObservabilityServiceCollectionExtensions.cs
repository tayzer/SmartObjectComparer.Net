using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ParityBench.NET.Application.Observability;

public static class ObservabilityServiceCollectionExtensions
{
    public static IServiceCollection AddParityBenchObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<ObservabilityOptions>? configureOverrides = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<ObservabilityOptions>()
            .Bind(configuration.GetSection("ParityBench:Observability"))
            .Configure(options => configureOverrides?.Invoke(options));
        services.AddSingleton<IObservabilityRecorder, ObservabilityRecorder>();
        return services;
    }
}
