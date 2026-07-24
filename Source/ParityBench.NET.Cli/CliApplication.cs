using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Composition;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Cli;

public static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter output,
        TextWriter error,
        string? workspaceRoot = null,
        Action<IServiceCollection>? configureServices = null,
        CancellationToken cancellationToken = default)
    {
        RequestCommandParseResult parseResult = RequestCommandParser.Parse(args);
        if (!parseResult.IsSuccess)
        {
            foreach (string parseError in parseResult.Errors)
            {
                await error.WriteLineAsync(parseError).ConfigureAwait(false);
            }

            await error.WriteLineAsync(RequestCommandParser.Usage).ConfigureAwait(false);
            return 2;
        }

        IConfiguration configuration = CreateConfiguration();
        ServiceCollection services = new ServiceCollection();
        RegisterServices(services, workspaceRoot ?? GetDefaultWorkspaceRoot(), configuration, parseResult.Options!.Observability);
        configureServices?.Invoke(services);

        await using ServiceProvider serviceProvider = services.BuildServiceProvider();

        // Seed run profiles from any installed plugin's templates, so a freshly
        // installed plugin is runnable via --run-profile with no manual setup.
        await serviceProvider
            .GetRequiredService<PluginProfileBootstrapper>()
            .EnsureTemplateProfilesAsync(cancellationToken)
            .ConfigureAwait(false);

        RequestCommandRunner runner = serviceProvider.GetRequiredService<RequestCommandRunner>();
        return await runner
            .RunAsync(parseResult.Options, output, error, cancellationToken)
            .ConfigureAwait(false);
    }

    public static void RegisterServices(
        IServiceCollection services,
        string workspaceRoot,
        IConfiguration configuration,
        ObservabilityCliOptions? observabilityOverrides = null)
    {
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging(builder =>
        {
            builder.AddConfiguration(configuration.GetSection("Logging"));
            builder.AddConsole();
            if (observabilityOverrides?.LogLevel is LogLevel logLevel)
            {
                builder.SetMinimumLevel(logLevel);
            }
        });

        string fixtureBaseUrl = configuration["ParityBench:RequestDefaults:FixtureBaseUrl"]
            ?? RequestComparisonFixtureDefaults.DefaultFixtureBaseUrl;

        services.AddParityBenchWorkspaceServices(
            configuration,
            workspaceRoot,
            fixtureBaseUrl,
            configureObservability: options =>
            {
                if (observabilityOverrides?.LogDurations == true)
                {
                    options.LogDurations = true;
                }

                if (observabilityOverrides?.LogExceptions == true)
                {
                    options.LogExceptions = true;
                }

                if (observabilityOverrides?.PersistDiagnostics == true)
                {
                    options.PersistDiagnostics = true;
                }

                if (observabilityOverrides?.SlowPathThresholdMs is int threshold)
                {
                    options.SlowPathThresholdMs = threshold;
                }
            });

        // The legacy ClientCustomerLookup contract-profile example is no longer wired
        // into the host. Its replacement is the ClientCustomerLookup plugin package,
        // discovered at run time from the workspace `plugins` folder and selected by a
        // run profile (`--run-profile`). The example code remains in the tree but is
        // not registered; see Docs/Guides/building-a-plugin.md.

        // Opt-in, config-gated: with Worker:Enabled=true, runs execute in a separate
        // worker process so client plugin code never loads into the host. Off by
        // default so the CLI does not require the worker binary to be deployed
        // alongside it for the built-in and raw comparison paths.
        if (configuration.GetValue("Worker:Enabled", false))
        {
            services.UseWorkerProcessExecution(configuration, workspaceRoot, fixtureBaseUrl);
        }

        services.AddSingleton<RequestCommandRunner>();
    }

    public static void RegisterServices(IServiceCollection services, string workspaceRoot) =>
        RegisterServices(services, workspaceRoot, CreateConfiguration());

    private static IConfiguration CreateConfiguration() =>
        new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables("PB_")
            .Build();

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

    private static string GetDefaultWorkspaceRoot() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ParityBench.NET",
            "Workspace");
}