using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using ComparisonTool.Cli.Infrastructure;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComparisonTool.Cli.Commands;

/// <summary>
/// CLI command for executing requests against two endpoints and comparing the responses.
/// </summary>
public static class RequestCompareCommand
{
    /// <summary>
    /// Creates the "request" sub-command.
    /// </summary>
    public static Command Create(IConfiguration configuration)
    {
        var requestDirArg = new Argument<DirectoryInfo>(
            "request-directory",
            "Path to a directory containing request body files (XML/JSON)");

        var endpointAOption = new Option<string>(
            aliases: new[] { "--endpoint-a", "-a" },
            description: "URL of the first endpoint")
        {
            IsRequired = true,
        };

        var endpointBOption = new Option<string>(
            aliases: new[] { "--endpoint-b", "-b" },
            description: "URL of the second endpoint")
        {
            IsRequired = true,
        };

        var modelOption = new Option<string>(
            aliases: new[] { "--model", "-m" },
            description: "Domain model name for response comparison (default: Auto)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        modelOption.SetDefaultValue("Auto");

        var concurrencyOption = new Option<int>(
            aliases: new[] { "--concurrency", "-c" },
            description: "Maximum concurrent requests (1-256)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        concurrencyOption.SetDefaultValue(64);
        concurrencyOption.AddValidator(result =>
        {
            var value = result.GetValueForOption(concurrencyOption);
            if (value < 1 || value > 256)
            {
                result.ErrorMessage = "Concurrency must be between 1 and 256";
            }
        });

        var timeoutOption = new Option<int>(
            aliases: new[] { "--timeout" },
            description: "Request timeout in milliseconds (1000-300000)")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        timeoutOption.SetDefaultValue(30000);

        var ignoreCollectionOrderOption = new Option<bool>(
            aliases: new[] { "--ignore-collection-order" },
            description: "Ignore collection ordering during comparison")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        ignoreCollectionOrderOption.SetDefaultValue(false);

        var ignoreCaseOption = new Option<bool>(
            aliases: new[] { "--ignore-case" },
            description: "Ignore string case during comparison")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        ignoreCaseOption.SetDefaultValue(false);

        var ignoreNamespacesOption = new Option<bool>(
            aliases: new[] { "--ignore-namespaces" },
            description: "Ignore XML namespaces during deserialization")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        ignoreNamespacesOption.SetDefaultValue(true);

        var semanticAnalysisOption = new Option<bool>(
            aliases: new[] { "--semantic-analysis" },
            description: "Enable semantic difference analysis")
        {
            Arity = ArgumentArity.ZeroOrOne,
        };
        semanticAnalysisOption.SetDefaultValue(true);

        var ignoreRulesFileOption = new Option<FileInfo?>(
            aliases: new[] { "--ignore-rules" },
            description: "Path to JSON file containing IgnoreRuleDto definitions");

        var contentTypeOption = new Option<string?>(
            aliases: new[] { "--content-type" },
            description: "Override Content-Type header for all request bodies");

        var outputOption = new Option<DirectoryInfo?>(
            aliases: new[] { "--output", "-o" },
            description: "Directory for report output files. Defaults to current directory");

        var formatOption = new Option<OutputFormat[]>(
            aliases: new[] { "--format", "-f" },
            description: "Output format(s): Console, Json, Markdown. Multiple allowed")
        {
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
        };
        formatOption.SetDefaultValue(new[] { OutputFormat.Console, });

        var command = new Command("request", "Execute requests against two endpoints and compare responses")
        {
            requestDirArg,
            endpointAOption,
            endpointBOption,
            modelOption,
            concurrencyOption,
            timeoutOption,
            ignoreCollectionOrderOption,
            ignoreCaseOption,
            ignoreNamespacesOption,
            semanticAnalysisOption,
            ignoreRulesFileOption,
            contentTypeOption,
            outputOption,
            formatOption,
        };

        command.SetHandler(async (context) =>
        {
            var requestDir = context.ParseResult.GetValueForArgument(requestDirArg);
            var endpointA = context.ParseResult.GetValueForOption(endpointAOption)!;
            var endpointB = context.ParseResult.GetValueForOption(endpointBOption)!;
            var model = context.ParseResult.GetValueForOption(modelOption)!;
            var concurrency = context.ParseResult.GetValueForOption(concurrencyOption);
            var timeout = context.ParseResult.GetValueForOption(timeoutOption);
            var ignoreCollectionOrder = context.ParseResult.GetValueForOption(ignoreCollectionOrderOption);
            var ignoreCase = context.ParseResult.GetValueForOption(ignoreCaseOption);
            var ignoreNamespaces = context.ParseResult.GetValueForOption(ignoreNamespacesOption);
            var semanticAnalysis = context.ParseResult.GetValueForOption(semanticAnalysisOption);
            var ignoreRulesFile = context.ParseResult.GetValueForOption(ignoreRulesFileOption);
            var contentTypeOverride = context.ParseResult.GetValueForOption(contentTypeOption);
            var outputDir = context.ParseResult.GetValueForOption(outputOption);
            var formats = context.ParseResult.GetValueForOption(formatOption) ?? new[] { OutputFormat.Console };
            var cancellationToken = context.GetCancellationToken();

            context.ExitCode = await ExecuteAsync(
                configuration,
                requestDir!,
                endpointA,
                endpointB,
                model,
                concurrency,
                timeout,
                ignoreCollectionOrder,
                ignoreCase,
                ignoreNamespaces,
                semanticAnalysis,
                ignoreRulesFile,
                contentTypeOverride,
                outputDir,
                formats,
                cancellationToken);
        });

        return command;
    }

    private static async Task<int> ExecuteAsync(
        IConfiguration configuration,
        DirectoryInfo requestDir,
        string endpointA,
        string endpointB,
        string modelName,
        int maxConcurrency,
        int timeoutMs,
        bool ignoreCollectionOrder,
        bool ignoreCase,
        bool ignoreNamespaces,
        bool semanticAnalysis,
        FileInfo? ignoreRulesFile,
        string? contentTypeOverride,
        DirectoryInfo? outputDir,
        OutputFormat[] formats,
        CancellationToken cancellationToken)
    {
        if (!requestDir.Exists)
        {
            Console.Error.WriteLine($"Request directory not found: {requestDir.FullName}");
            return 1;
        }

        Console.WriteLine("Request comparison:");
        Console.WriteLine($"  Requests:    {requestDir.FullName}");
        Console.WriteLine($"  Endpoint A:  {endpointA}");
        Console.WriteLine($"  Endpoint B:  {endpointB}");
        Console.WriteLine($"  Model:       {modelName}");
        Console.WriteLine($"  Concurrency: {maxConcurrency}");
        if (!string.IsNullOrWhiteSpace(contentTypeOverride))
        {
            Console.WriteLine($"  Content-Type: {contentTypeOverride}");
        }
        Console.WriteLine();

        // Stage the request files into the temp batch directory that RequestFileParserService expects
        var batchId = await StageRequestBatchAsync(requestDir, cancellationToken);

        await using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);

        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var ignoreRulesResult = await LoadIgnoreRulesAsync(ignoreRulesFile, cancellationToken);
        if (!ignoreRulesResult.IsSuccess)
        {
            Console.Error.WriteLine(ignoreRulesResult.ErrorMessage);
            return 1;
        }

        var createRequest = new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = endpointA,
            EndpointB = endpointB,
            TimeoutMs = timeoutMs,
            MaxConcurrency = maxConcurrency,
            ModelName = modelName,
            IgnoreCollectionOrder = ignoreCollectionOrder,
            IgnoreStringCase = ignoreCase,
            IgnoreXmlNamespaces = ignoreNamespaces,
            EnableSemanticAnalysis = semanticAnalysis,
            IgnoreRules = ignoreRulesResult.IgnoreRules,
            SmartIgnoreRules = ignoreRulesResult.SmartIgnoreRules,
            ContentTypeOverride = contentTypeOverride,
        };

        var job = jobService.CreateJob(createRequest);

        var progress = new Progress<(int Completed, int Total, string Message)>(p =>
        {
            // Progress updates are handled by ConsoleProgressPublisher via IComparisonProgressPublisher
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await jobService.ExecuteJobAsync(job.JobId, progress, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine();
            Console.Error.WriteLine("Job was cancelled.");
            return 1;
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.Error.WriteLine($"Job failed: {ex.Message}");
            return 1;
        }

        stopwatch.Stop();
        Console.WriteLine();

        var result = jobService.GetResult(job.JobId);
        if (result == null)
        {
            Console.Error.WriteLine("No comparison result was produced.");
            return 1;
        }

        var reportContext = new ReportContext
        {
            Result = result,
            Elapsed = stopwatch.Elapsed,
            CommandName = "request",
            EndpointA = endpointA,
            EndpointB = endpointB,
            ModelName = modelName,
            JobId = job.JobId,
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
                    var jsonPath = Path.Combine(resolvedOutputDir, $"request-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    await JsonReportWriter.WriteAsync(reportContext, jsonPath);
                    Console.WriteLine($"  JSON report: {jsonPath}");
                    break;
                case OutputFormat.Markdown:
                    var mdPath = Path.Combine(resolvedOutputDir, $"request-comparison-{DateTime.Now:yyyyMMdd-HHmmss}.md");
                    await MarkdownReportWriter.WriteAsync(reportContext, mdPath);
                    Console.WriteLine($"  Markdown report: {mdPath}");
                    break;
            }
        }

        return result.AllEqual ? 0 : 2;
    }

    /// <summary>
    /// Copies request files from the user's directory into the temp batch path
    /// that <see cref="RequestFileParserService"/> expects.
    /// </summary>
    private static async Task<string> StageRequestBatchAsync(
        DirectoryInfo requestDir,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid().ToString("N")[..12];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);

        var files = requestDir.GetFiles("*.*", SearchOption.TopDirectoryOnly)
            .Where(f => f.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
                     || f.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (files.Count == 0)
        {
            throw new InvalidOperationException(
                $"No request files (xml/json/txt) found in {requestDir.FullName}");
        }

        Console.WriteLine($"  Staging {files.Count} request file(s)...");

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destPath = Path.Combine(batchPath, file.Name);
            await CopyFileAsync(file.FullName, destPath, cancellationToken);
        }

        return batchId;
    }

    private static async Task CopyFileAsync(string source, string destination, CancellationToken cancellationToken)
    {
        const int bufferSize = 81920;
        await using var sourceStream = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize, useAsync: true);
        await using var destStream = new FileStream(destination, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize, useAsync: true);
        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken);
    }

    private static async Task<IgnoreRulesLoadResult> LoadIgnoreRulesAsync(
        FileInfo? fileInfo,
        CancellationToken cancellationToken)
    {
        if (fileInfo == null)
        {
            return IgnoreRulesLoadResult.Success(null, null);
        }

        if (!fileInfo.Exists)
        {
            return IgnoreRulesLoadResult.Failure($"Ignore rules file not found: {fileInfo.FullName}");
        }

        try
        {
            var json = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
            };

            if (string.IsNullOrWhiteSpace(json))
            {
                return IgnoreRulesLoadResult.Success(new List<IgnoreRuleDto>(), new List<SmartIgnoreRuleDto>());
            }

            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var rules = JsonSerializer.Deserialize<List<IgnoreRuleDto>>(json, options) ?? new List<IgnoreRuleDto>();
                return IgnoreRulesLoadResult.Success(rules, new List<SmartIgnoreRuleDto>());
            }

            var container = JsonSerializer.Deserialize<IgnoreRulesContainer>(json, options) ?? new IgnoreRulesContainer();
            return IgnoreRulesLoadResult.Success(
                container.IgnoreRules ?? new List<IgnoreRuleDto>(),
                container.SmartIgnoreRules ?? new List<SmartIgnoreRuleDto>());
        }
        catch (JsonException ex)
        {
            return IgnoreRulesLoadResult.Failure($"Invalid ignore rules JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return IgnoreRulesLoadResult.Failure($"Failed to read ignore rules file: {ex.Message}");
        }
    }

    private sealed class IgnoreRulesContainer
    {
        public List<IgnoreRuleDto>? IgnoreRules { get; init; }

        public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }
    }

    private sealed record IgnoreRulesLoadResult
    {
        public bool IsSuccess { get; init; }

        public string? ErrorMessage { get; init; }

        public List<IgnoreRuleDto>? IgnoreRules { get; init; }

        public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }

        public static IgnoreRulesLoadResult Success(
            List<IgnoreRuleDto>? ignoreRules,
            List<SmartIgnoreRuleDto>? smartIgnoreRules)
            => new IgnoreRulesLoadResult
            {
                IsSuccess = true,
                IgnoreRules = ignoreRules,
                SmartIgnoreRules = smartIgnoreRules,
            };

        public static IgnoreRulesLoadResult Failure(string message)
            => new IgnoreRulesLoadResult
            {
                IsSuccess = false,
                ErrorMessage = message,
            };
    }
}
