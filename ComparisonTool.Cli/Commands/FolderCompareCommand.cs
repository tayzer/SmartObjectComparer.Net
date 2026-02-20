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
        var dir1Arg = new Argument<DirectoryInfo>("directory1") { Description = "Path to the first (expected) directory" };
        var dir2Arg = new Argument<DirectoryInfo>("directory2") { Description = "Path to the second (actual) directory" };

        var modelOption = new Option<string>("--model", "-m")
        {
            Description = "Domain model name for deserialization (e.g. ComplexOrderResponse, SoapEnvelope)",
            Required = true,
        };

        var includeAllOption = new Option<bool>("--include-all")
        {
            Description = "Include files that exist in only one directory",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var patternAnalysisOption = new Option<bool>("--pattern-analysis")
        {
            Description = "Enable pattern analysis on results",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => true,
        };

        var semanticAnalysisOption = new Option<bool>("--semantic-analysis")
        {
            Description = "Enable semantic difference analysis",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => true,
        };

        var ignoreTrailingWhitespaceOption = new Option<bool>("--ignore-trailing-whitespace-end")
        {
            Description = "Ignore trailing spaces and tabs at the end of strings during comparison",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var outputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Directory for report output files (JSON/Markdown). Defaults to current directory",
        };

        var formatOption = new Option<OutputFormat[]>("--format", "-f")
        {
            Description = "Output format(s): Console, Json, Markdown. Multiple allowed",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => new[] { OutputFormat.Console },
        };

        var pageSizeOption = new Option<int>("--page-size")
        {
            Description = "Max file pairs per markdown page (0 = no pagination, all in one file)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => 50,
        };
        pageSizeOption.Validators.Add(result =>
        {
            var value = result.GetValue(pageSizeOption);
            if (value < 0)
            {
                result.AddError("Page size must be 0 (no pagination) or a positive number");
            }
        });

        var command = new Command("folder", "Compare two directories of XML/JSON files")
        {
            dir1Arg,
            dir2Arg,
            modelOption,
            includeAllOption,
            patternAnalysisOption,
            semanticAnalysisOption,
            ignoreTrailingWhitespaceOption,
            outputOption,
            formatOption,
            pageSizeOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var dir1 = parseResult.GetValue(dir1Arg);
            var dir2 = parseResult.GetValue(dir2Arg);
            var model = parseResult.GetValue(modelOption)!;
            var includeAll = parseResult.GetValue(includeAllOption);
            var patternAnalysis = parseResult.GetValue(patternAnalysisOption);
            var semanticAnalysis = parseResult.GetValue(semanticAnalysisOption);
            var ignoreTrailingWhitespaceAtEnd = parseResult.GetValue(ignoreTrailingWhitespaceOption);
            var outputDir = parseResult.GetValue(outputOption);
            var formats = parseResult.GetValue(formatOption) ?? new[] { OutputFormat.Console };
            var pageSize = parseResult.GetValue(pageSizeOption);

            return await ExecuteAsync(
                configuration,
                dir1!,
                dir2!,
                model,
                includeAll,
                patternAnalysis,
                semanticAnalysis,
                ignoreTrailingWhitespaceAtEnd,
                outputDir,
                formats,
                pageSize,
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
        bool ignoreTrailingWhitespaceAtEnd,
        DirectoryInfo? outputDir,
        OutputFormat[] formats,
        int markdownPageSize,
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

        var configService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();
        var comparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

        configService.SetIgnoreTrailingWhitespaceAtEnd(ignoreTrailingWhitespaceAtEnd);

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
            MarkdownPageSize = markdownPageSize,
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
                    var pageCount = await MarkdownReportWriter.WriteAsync(reportContext, mdPath);
                    var pageSuffix = pageCount > 0 ? $" (+{pageCount} detail pages)" : string.Empty;
                    Console.WriteLine($"  Markdown report: {mdPath}{pageSuffix}");
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
