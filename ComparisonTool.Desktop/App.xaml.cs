using System.Windows;
using ComparisonTool.Core.Abstractions;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Desktop.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Serilog;
using Blazored.LocalStorage;

namespace ComparisonTool.Desktop;

/// <summary>
/// WPF Application entry point. Configures DI and hosts the BlazorWebView.
/// </summary>
public partial class App : System.Windows.Application
{
    /// <summary>
    /// Gets the application-wide service provider.
    /// </summary>
    public static IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
            .Build();

        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .WriteTo.File("Logs/desktop-.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .CreateLogger();

        var services = new ServiceCollection();

        // Logging
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddSerilog(Log.Logger);
        });

        // Configuration
        services.AddSingleton<IConfiguration>(configuration);

        // Core comparison services (same registration as Web and CLI)
        services.AddUnifiedComparisonServices(configuration, options =>
        {
            options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope");
        });

        // MudBlazor
        services.AddMudServices();

        // Local Storage
        services.AddBlazoredLocalStorage();

        // WPF + Blazor
        services.AddWpfBlazorWebView();
#if DEBUG
        services.AddBlazorWebViewDeveloperTools();
#endif

        // HTTP client for request comparison
        services.AddHttpClient("RequestComparison")
            .ConfigureHttpClient(client =>
            {
                client.Timeout = TimeSpan.FromMinutes(5);
            });

        // Request comparison services (in-process, no HTTP API layer)
        services.AddSingleton<RequestFileParserService>();
        services.AddSingleton<RequestExecutionService>();
        services.AddSingleton<RawTextComparisonService>();
        services.AddSingleton<RequestComparisonJobService>();
        services.AddScoped<ComparisonTool.Core.RequestComparison.Services.RawContentService>();

        // Desktop platform service implementations
        services.AddSingleton<IFileExportService, DesktopFileExportService>();
        services.AddSingleton<IFolderPickerService, DesktopFolderPickerService>();
        services.AddSingleton<INotificationService, DesktopNotificationService>();
        services.AddScoped<IScrollService, BlazorScrollService>(); // Scoped: depends on IJSRuntime
        services.AddSingleton<InProcessProgressPublisher>();
        services.AddSingleton<IComparisonProgressPublisher>(sp => sp.GetRequiredService<InProcessProgressPublisher>());
        services.AddScoped<IProgressSubscriber, InProcessProgressSubscriber>();
        services.AddSingleton<IRequestComparisonGateway, InProcessRequestComparisonGateway>();

        Services = services.BuildServiceProvider();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Log.CloseAndFlush();
        base.OnExit(e);
    }
}
