using System.CommandLine;
using System.Diagnostics;
using ComparisonTool.Cli.Infrastructure;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComparisonTool.Cli.Commands;

/// <summary>
/// CLI command for comparing two directories of XML/JSON files.
/// </summary>
public static class FolderCompareCommand
{
    /// <summary>
    /// Creates the "folder" sub-command.
    /// </summary>
    public static Command Create(IConfiguration configuration)
    {
        var dir1Arg = new Argument<DirectoryInfo>("directory1", "Path to the first (expected) directory");
        var dir2Arg = new Argument<DirectoryInfo>("directory2", "Path to the second (actual) directory");

        var modelOption = new Option<string>(
            aliases: new[] { "--model", "-m" },
            description: "Domain model name for deserialization (e.g. ComplexOrderResponse, SoapEnvelope)")
        {
            IsRequired = true,
        };

        var includeAllOption = new Option<bool>(
            aliases: new[] { "--include-all" },
            description: "Include files that exist in only one directory")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        includeAllOption.SetDefaultValue(false);

        var patternAnalysisOption = new Option<bool>(
            aliases: new[] { "--pattern-analysis" },
            description: "Enable pattern analysis on results")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        patternAnalysisOption.SetDefaultValue(true);

        var semanticAnalysisOption = new Option<bool>(
            aliases: new[] { "--semantic-analysis" },
            description: "Enable semantic difference analysis")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        semanticAnalysisOption.SetDefaultValue(true);

        var outputOption = new Option<DirectoryInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Directory for report output files (JSON/Markdown). Defaults to current directory");

        var formatOption = new Option<OutputFormat[]>(
            aliases: new[] { "--format", "-f", },
            description: "Output format(s): Console, Json, Markdown. Multiple allowed")
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        formatOption.SetDefaultValue(new[] { OutputFormat.Console, });

        var command = new Command("folder", "Compare two directories of XML/JSON files")
        {
            dir1Arg,
            dir2Arg,
            modelOption,
            includeAllOption,
            patternAnalysisOption,
            semanticAnalysisOption,
            outputOption,
            formatOption,
        };

        command.SetHandler(async (context) =>
        {
            var dir1 = context.ParseResult.GetValueForArgument(dir1Arg);
            var dir2 = context.ParseResult.GetValueForArgument(dir2Arg);
            var model = context.ParseResult.GetValueForOption(modelOption)!;
            var includeAll = context.ParseResult.GetValueForOption(includeAllOption);
            var patternAnalysis = context.ParseResult.GetValueForOption(patternAnalysisOption);
            var semanticAnalysis = context.ParseResult.GetValueForOption(semanticAnalysisOption);
            var outputDir = context.ParseResult.GetValueForOption(outputOption);
            var formats = context.ParseResult.GetValueForOption(formatOption) ?? new[] { OutputFormat.Console };
            var cancellationToken = context.GetCancellationToken();

            context.ExitCode = await ExecuteAsync(
                configuration,
                dir1!,
                dir2!,
                model,
                includeAll,
                patternAnalysis,
                semanticAnalysis,
                outputDir,
                formats,
                cancellationToken);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        IConfiguration configuration,
        DirectoryInfo dir1,
        DirectoryInfo dir2,
        string modelName,
        bool includeAll,
        bool patternAnalysis,
        bool semanticAnalysis,
        DirectoryInfo? outputDir,
        OutputFormat[] formats,
        CancellationToken cancellationToken)
    {
        if (!dir1.Exists)
        {
            Console.Error.WriteLine($"Directory not found: {dir1.FullName}");
            return 1;
        }

        if (!dir2.Exists)
        {
            Console.Error.WriteLine($"Directory not found: {dir2.FullName}");
            return 1;
        }

        Console.WriteLine($"Comparing folders:");
        Console.WriteLine($"  Directory 1: {dir1.FullName}");
        Console.WriteLine($"  Directory 2: {dir2.FullName}");
        Console.WriteLine($"  Model:       {modelName}");
        Console.WriteLine();

        await using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);
        using var scope = serviceProvider.CreateScope();

        var comparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

        var progress = new Progress<ComparisonProgress>(p =>
        {
            var bar = BuildProgressBar(p.Total > 0 ? (int)(100.0 * p.Completed / p.Total) : 0);
            var line = $"\r  {bar} {p.Completed}/{p.Total} {p.Status}";
            Console.Write(line.PadRight(Console.BufferWidth > 0 ? Math.Min(Console.BufferWidth - 1, 120) : 120));
        });

        var stopwatch = Stopwatch.StartNew();

        var result = await comparisonService.CompareDirectoriesAsync(
            dir1.FullName,
            dir2.FullName,
            modelName,
            includeAll,
            patternAnalysis,
            semanticAnalysis,
            progress,
            cancellationToken);

        stopwatch.Stop();
        Console.WriteLine(); // newline after progress bar

        var reportContext = new ReportContext
        {
            Result = result,
            Elapsed = stopwatch.Elapsed,
            CommandName = "folder",
            Directory1 = dir1.FullName,
            Directory2 = dir2.FullName,
            ModelName = modelName,
            MostAffectedFields = MostAffectedFieldsAggregator.Build(result),
        };

        var resolvedOutputDir = outputDir?.FullName ?? Directory.GetCurrentDirectory();
        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var format in formats.Distinct())
        {
            switch (format)
            {
                case OutputFormat.Console:
                    ConsoleReportWriter.Write(reportContext);
                    break;
                case OutputFormat.Json:
                    var jsonPath = Path.Combine(resolvedOutputDir, $"comparison-result-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    await JsonReportWriter.WriteAsync(reportContext, jsonPath);
                    Console.WriteLine($"  JSON report: {jsonPath}");
                    break;
                case OutputFormat.Markdown:
                    var mdPath = Path.Combine(resolvedOutputDir, $"comparison-result-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                    await MarkdownReportWriter.WriteAsync(reportContext, mdPath);
                    Console.WriteLine($"  Markdown report: {mdPath}");
                    break;
            }
        }

        return result.AllEqual ? 0 : 2;
    }

    private static string BuildProgressBar(int percent)
    {
        const int width = 20;
        var filled = (int)(percent / 100.0 * width);
        var empty = width - filled;
        return $"[{new string('#', filled)}{new string('-', empty)}]";
    }
}
