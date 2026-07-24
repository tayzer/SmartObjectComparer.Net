using System.IO;
using System.Windows;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;
using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Composition;
using ParityBench.NET.Desktop.Services;
using ParityBench.NET.Infrastructure;
using ParityBench.NET.UI.Theming;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        host = Host.CreateDefaultBuilder(e.Args)
            .ConfigureServices((context, services) =>
            {
                services.AddWpfBlazorWebView();
                services.AddMudServices();
                services.AddScoped<ParityBenchThemeState>();

                string workspaceRoot = context.Configuration["ParityBench:WorkspaceRoot"]
                    ?? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ParityBench.NET",
                        "Workspace");

                string fixtureBaseUrl = context.Configuration["ParityBench:RequestDefaults:FixtureBaseUrl"]
                    ?? RequestComparisonFixtureDefaults.DefaultFixtureBaseUrl;

                RegisterV2Services(services, workspaceRoot, fixtureBaseUrl, context.Configuration["ParityBench:AcceptedDifferences:StorePath"], context.Configuration);
            })
            .Build();

        await host.StartAsync();

        MainWindow mainWindow = new MainWindow(host.Services);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync();
            host.Dispose();
        }

        base.OnExit(e);
    }

    private static string FindManualRunRoot()
    {
        foreach (string candidateRoot in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
        {
            DirectoryInfo? directory = new DirectoryInfo(candidateRoot);
            while (directory is not null)
            {
                string manualRunRoot = Path.Combine(directory.FullName, "Examples", "ParityBench.NET.ManualRuns");
                if (Directory.Exists(manualRunRoot))
                {
                    return manualRunRoot;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine("Examples", "ParityBench.NET.ManualRuns");
    }
    private static void RegisterV2Services(IServiceCollection services, string workspaceRoot, string fixtureBaseUrl, string? acceptedDifferenceStorePath, IConfiguration configuration)
    {
        services.AddParityBenchWorkspaceServices(
            configuration,
            workspaceRoot,
            fixtureBaseUrl,
            configureRequestComparisonFixtures: (svc, endpointDefaults, presetDefaults) =>
                svc.AddClientCustomerLookupExample(configuration, endpointDefaults, presetDefaults, FindManualRunRoot()));
        // Opt-in via Worker:Enabled=true: execute runs out of process so a plugin
        // failure cannot take the desktop app down.
        if (configuration.GetValue("Worker:Enabled", false))
        {
            services.UseWorkerProcessExecution(configuration, workspaceRoot, fixtureBaseUrl);
        }

        services.AddParityBenchUiServices(workspaceRoot, acceptedDifferenceStorePath);
        services.AddScoped<IRequestSourcePicker, DesktopRequestSourcePicker>();
    }
}
