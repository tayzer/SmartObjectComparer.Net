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
        var requestDirArg = new Argument<DirectoryInfo>("request-directory")
        {
            Description = "Path to a directory containing request body files (XML/JSON)",
        };

        var endpointAOption = new Option<string>("--endpoint-a", "-a")
        {
            Description = "URL of the first endpoint",
            Required = true,
        };

        var endpointBOption = new Option<string>("--endpoint-b", "-b")
        {
            Description = "URL of the second endpoint",
            Required = true,
        };

        var modelOption = new Option<string>("--model", "-m")
        {
            Description = "Domain model name for response comparison (default: Auto)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => "Auto",
        };

        var concurrencyOption = new Option<int>("--concurrency", "-c")
        {
            Description = "Maximum concurrent requests (1-256)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => 64,
        };
        concurrencyOption.Validators.Add(result =>
        {
            var value = result.GetValue(concurrencyOption);
            if (value < 1 || value > 256)
            {
                result.AddError("Concurrency must be between 1 and 256");
            }
        });

        var timeoutOption = new Option<int>("--timeout")
        {
            Description = "Request timeout in milliseconds (1000-300000)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => 30000,
        };

        var ignoreCollectionOrderOption = new Option<bool>("--ignore-collection-order")
        {
            Description = "Ignore collection ordering during comparison",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var ignoreCaseOption = new Option<bool>("--ignore-case")
        {
            Description = "Ignore string case during comparison",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var ignoreTrailingWhitespaceOption = new Option<bool>("--ignore-trailing-whitespace-end")
        {
            Description = "Ignore trailing spaces and tabs at the end of strings during comparison",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var ignoreNamespacesOption = new Option<bool>("--ignore-namespaces")
        {
            Description = "Ignore XML namespaces during deserialization",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => true,
        };

        var semanticAnalysisOption = new Option<bool>("--semantic-analysis")
        {
            Description = "Enable semantic difference analysis",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => true,
        };

        var ignoreRulesFileOption = new Option<FileInfo?>("--ignore-rules")
        {
            Description = "Path to JSON file containing IgnoreRuleDto definitions",
        };

        var contentTypeOption = new Option<string?>("--content-type")
        {
            Description = "Override Content-Type header for all request bodies",
        };

        var outputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Directory for report output files. Defaults to current directory",
        };

        var formatOption = new Option<OutputFormat[]>("--format", "-f")
        {
            Description = "Output format(s): Console, Json, Markdown, Html. Multiple allowed",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => new[] { OutputFormat.Console },
        };

        var htmlModeOption = new Option<HtmlReportMode>("--html-mode")
        {
            Description = "HTML output mode: SingleFile or StaticSite",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => HtmlReportMode.SingleFile,
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
            ignoreTrailingWhitespaceOption,
            ignoreNamespacesOption,
            semanticAnalysisOption,
            ignoreRulesFileOption,
            contentTypeOption,
            outputOption,
            formatOption,
            htmlModeOption,
            pageSizeOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestDir = parseResult.GetValue(requestDirArg);
            var endpointA = parseResult.GetValue(endpointAOption)!;
            var endpointB = parseResult.GetValue(endpointBOption)!;
            var model = parseResult.GetValue(modelOption)!;
            var concurrency = parseResult.GetValue(concurrencyOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var ignoreCollectionOrder = parseResult.GetValue(ignoreCollectionOrderOption);
            var ignoreCase = parseResult.GetValue(ignoreCaseOption);
            var ignoreTrailingWhitespaceAtEnd = parseResult.GetValue(ignoreTrailingWhitespaceOption);
            var ignoreNamespaces = parseResult.GetValue(ignoreNamespacesOption);
            var semanticAnalysis = parseResult.GetValue(semanticAnalysisOption);
            var ignoreRulesFile = parseResult.GetValue(ignoreRulesFileOption);
            var contentTypeOverride = parseResult.GetValue(contentTypeOption);
            var outputDir = parseResult.GetValue(outputOption);
            var formats = parseResult.GetValue(formatOption) ?? new[] { OutputFormat.Console };
            var htmlMode = parseResult.GetValue(htmlModeOption);
            var pageSize = parseResult.GetValue(pageSizeOption);

            return await ExecuteAsync(
                configuration,
                requestDir!,
                endpointA,
                endpointB,
                model,
                concurrency,
                timeout,
                ignoreCollectionOrder,
                ignoreCase,
                ignoreTrailingWhitespaceAtEnd,
                ignoreNamespaces,
                semanticAnalysis,
                ignoreRulesFile,
                contentTypeOverride,
                outputDir,
                formats,
                htmlMode,
                pageSize,
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
        bool ignoreTrailingWhitespaceAtEnd,
        bool ignoreNamespaces,
        bool semanticAnalysis,
        FileInfo? ignoreRulesFile,
        string? contentTypeOverride,
        DirectoryInfo? outputDir,
        OutputFormat[] formats,
        HtmlReportMode htmlMode,
        int markdownPageSize,
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
            IgnoreTrailingWhitespaceAtEnd = ignoreTrailingWhitespaceAtEnd,
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
            MostAffectedFields = MostAffectedFieldsAggregator.Build(result),
            MarkdownPageSize = markdownPageSize,
            HtmlMode = htmlMode,
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
                    var pageCount = await MarkdownReportWriter.WriteAsync(reportContext, mdPath);
                    var pageSuffix = pageCount > 0 ? $" (+{pageCount} detail pages)" : string.Empty;
                    Console.WriteLine($"  Markdown report: {mdPath}{pageSuffix}");
                    break;
                case OutputFormat.Html:
                    var htmlTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    var htmlOutputPath = htmlMode == HtmlReportMode.StaticSite
                        ? Path.Combine(resolvedOutputDir, $"request-comparison-{htmlTimestamp}")
                        : Path.Combine(resolvedOutputDir, $"request-comparison-{htmlTimestamp}.html");
                    var htmlResult = await HtmlReportWriter.WriteAsync(reportContext, htmlOutputPath);
                    var detailSuffix = htmlResult.DetailPageCount > 0
                        ? $" (+{htmlResult.DetailPageCount} pair pages)"
                        : string.Empty;
                    Console.WriteLine($"  HTML report: {htmlResult.PrimaryPath}{detailSuffix}");
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
