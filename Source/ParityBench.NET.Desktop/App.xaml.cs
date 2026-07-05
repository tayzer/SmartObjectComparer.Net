using System.Windows;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MudBlazor.Services;

using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.UI.Results;
using ParityBench.NET.Workspaces;

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

                string workspaceRoot = context.Configuration["ParityBench:WorkspaceRoot"]
                    ?? System.IO.Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "ParityBench.NET",
                        "Workspace");

                services.AddSingleton<IRunStore>(_ => new FileSystemRunStore(workspaceRoot));
                services.AddSingleton<IRunDetailStore>(_ => new FileSystemRunDetailStore(workspaceRoot));
                services.AddSingleton<IRunArtifactStore>(_ => new FileSystemRunArtifactStore(workspaceRoot));
                services.AddScoped<IComparisonRunResultUseCases, ComparisonRunResultService>();
                services.AddScoped<IRunResultsViewDataSource, ApplicationRunResultsViewDataSource>();
            })
            .Build();

        await host.StartAsync().ConfigureAwait(false);

        MainWindow mainWindow = new MainWindow(host.Services);
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (host is not null)
        {
            await host.StopAsync().ConfigureAwait(false);
            host.Dispose();
        }

        base.OnExit(e);
    }
}