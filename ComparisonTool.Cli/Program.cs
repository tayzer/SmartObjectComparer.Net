using System.CommandLine;
using ComparisonTool.Cli.Commands;
using ComparisonTool.Cli.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ComparisonTool.Cli;

/// <summary>
/// Entry point for the ComparisonTool CLI.
/// </summary>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        // Build configuration early so Serilog can read from it
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
            .AddEnvironmentVariables("CT_")
            .Build();

        Log.Logger = new LoggerConfiguration()
            .ReadFrom.Configuration(configuration)
            .Enrich.FromLogContext()
            .CreateLogger();

        try
        {
            var rootCommand = BuildRootCommand(configuration);
            return await rootCommand.InvokeAsync(args);
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "CLI terminated unexpectedly");
            Console.Error.WriteLine($"Fatal error: {ex.Message}");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    /// <summary>
    /// Builds the root command with all sub-commands.
    /// </summary>
    internal static RootCommand BuildRootCommand(IConfiguration configuration)
    {
        var rootCommand = new RootCommand("ComparisonTool CLI — compare XML/JSON files, folders, and HTTP endpoint responses")
        {
            FolderCompareCommand.Create(configuration),
            RequestCompareCommand.Create(configuration),
        };

        return rootCommand;
    }
}
