using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ComparisonTool.Cli.Infrastructure;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.RequestComparison.Models;
using ComparisonTool.Core.RequestComparison.Services;
using ComparisonTool.Core.Serialization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ComparisonTool.Cli.Commands;

/// <summary>
/// CLI command for executing requests against two endpoints and comparing the responses.
/// </summary>
public static partial class RequestCompareCommand
{
    private static readonly Regex RequestRangePattern = new Regex(
        @"^\s*(?<start>-?\d+)\s*-\s*(?<end>-?\d+)\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>
    /// Creates the "request" sub-command.
    /// </summary>
    public static Command Create(IConfiguration configuration)
    {
        var requestDirArg = new Argument<DirectoryInfo>("request-directory")
        {
            Description = "Path to a directory containing request body files (XML/JSON/TXT)",
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
            Description = "Domain model name for response comparison. Must match a registered model (e.g. ComplexOrderResponse, SoapEnvelope)",
            Required = true,
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

        var maskRulesFileOption = new Option<FileInfo?>("--mask-rules")
        {
            Description = "Path to JSON file containing MaskRuleDto definitions for response masking",
        };

        var contentTypeOption = new Option<string?>("--content-type")
        {
            Description = "Override Content-Type header for all request bodies",
        };

        var soapActionOption = new Option<string?>("--soap-action")
        {
            Description = "Optional SOAPAction header value to send with every request",
        };

        var rangeOption = new Option<string?>("--range")
        {
            Description = "1-based inclusive ordinal range of request files after ordinal sorting, for example 1-500",
        };

        var outputOption = new Option<DirectoryInfo?>("--output", "-o")
        {
            Description = "Directory for report output files. Defaults to current directory",
        };

        var formatOption = new Option<OutputFormat[]>("--format", "-f")
        {
            Description = "Output format(s): Console, Json, Html, Markdown. Multiple allowed",
            Arity = ArgumentArity.OneOrMore,
            AllowMultipleArgumentsPerToken = true,
            DefaultValueFactory = _ => new[] { OutputFormat.Console },
        };

        var htmlModeOption = new Option<HtmlReportMode>("--html-mode")
        {
            Description = "HTML export mode: StaticSite (lazy-loaded static site) or SingleFile (embedded payload)",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => HtmlReportMode.StaticSite,
        };

        var htmlChunkSizeOption = new Option<int>("--html-chunk-size")
        {
            Description = "Pairs per lazy-loaded HTML detail chunk",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => 250,
        };
        htmlChunkSizeOption.Validators.Add(result =>
        {
            var value = result.GetValue(htmlChunkSizeOption);
            if (value < 25 || value > 1000)
            {
                result.AddError("HTML chunk size must be between 25 and 1000");
            }
        });

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

        var disableTruncationOption = new Option<bool>("--disable-truncation")
        {
            Description = "Disable truncation of long strings in reports",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

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
            maskRulesFileOption,
            contentTypeOption,
            soapActionOption,
            rangeOption,
            outputOption,
            formatOption,
            htmlModeOption,
            htmlChunkSizeOption,
            pageSizeOption,
            disableTruncationOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestDir = parseResult.GetValue(requestDirArg);
            var endpointA = parseResult.GetValue(endpointAOption)
                ?? throw new InvalidOperationException("Missing required option --endpoint-a.");
            var endpointB = parseResult.GetValue(endpointBOption)
                ?? throw new InvalidOperationException("Missing required option --endpoint-b.");
            var model = parseResult.GetValue(modelOption)
                ?? throw new InvalidOperationException("Missing required option --model.");
            var concurrency = parseResult.GetValue(concurrencyOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var ignoreCollectionOrder = parseResult.GetValue(ignoreCollectionOrderOption);
            var ignoreCase = parseResult.GetValue(ignoreCaseOption);
            var ignoreTrailingWhitespaceAtEnd = parseResult.GetValue(ignoreTrailingWhitespaceOption);
            var ignoreNamespaces = parseResult.GetValue(ignoreNamespacesOption);
            var semanticAnalysis = parseResult.GetValue(semanticAnalysisOption);
            var ignoreRulesFile = parseResult.GetValue(ignoreRulesFileOption);
            var maskRulesFile = parseResult.GetValue(maskRulesFileOption);
            var contentTypeOverride = parseResult.GetValue(contentTypeOption);
            var soapAction = parseResult.GetValue(soapActionOption);
            var requestRange = parseResult.GetValue(rangeOption);
            var outputDir = parseResult.GetValue(outputOption);
            var formats = parseResult.GetValue(formatOption) ?? new[] { OutputFormat.Console };
            var htmlMode = parseResult.GetValue(htmlModeOption);
            var htmlChunkSize = parseResult.GetValue(htmlChunkSizeOption);
            var pageSize = parseResult.GetValue(pageSizeOption);
            var disableTruncation = parseResult.GetValue(disableTruncationOption);

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
                maskRulesFile,
                contentTypeOverride,
                soapAction,
                requestRange,
                outputDir,
                formats,
                htmlMode,
                htmlChunkSize,
                pageSize,
                disableTruncation,
                cancellationToken);
        });

        return command;
    }

    internal static RequestBatchSelection CreateRequestBatchSelection(
        DirectoryInfo requestDir,
        string? rangeText)
    {
        var eligibleFiles = GetEligibleRequestFiles(requestDir);

        if (eligibleFiles.Count == 0)
        {
            throw new InvalidOperationException(
                $"No request files (xml/json/txt) found in {requestDir.FullName}");
        }

        var requestedRange = ParseRequestRange(rangeText);
        var appliedRange = ApplyRequestRange(requestedRange, eligibleFiles.Count);

        var selectedFiles = eligibleFiles
            .Skip(appliedRange.StartOrdinal - 1)
            .Take(appliedRange.EndOrdinal - appliedRange.StartOrdinal + 1)
            .ToList();

        return new RequestBatchSelection(
            eligibleFiles.Count,
            selectedFiles,
            requestedRange,
            appliedRange);
    }

    internal static RequestOrdinalRange? ParseRequestRange(string? rangeText)
    {
        if (string.IsNullOrWhiteSpace(rangeText))
        {
            return null;
        }

        var match = RequestRangePattern.Match(rangeText);
        if (!match.Success
            || !int.TryParse(match.Groups["start"].Value, out var startOrdinal)
            || !int.TryParse(match.Groups["end"].Value, out var endOrdinal))
        {
            throw new ArgumentException(
                $"Invalid --range value '{rangeText}'. Expected format 'start-end' using 1-based inclusive ordinals, for example '1-500'.",
                nameof(rangeText));
        }

        if (startOrdinal <= 0 || endOrdinal <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(rangeText),
                $"Invalid --range value '{rangeText}'. Range values must be positive 1-based ordinals.");
        }

        if (startOrdinal > endOrdinal)
        {
            throw new ArgumentException(
                $"Invalid --range value '{rangeText}'. Range start must be less than or equal to range end.",
                nameof(rangeText));
        }

        return new RequestOrdinalRange(startOrdinal, endOrdinal);
    }

    internal static IReadOnlyList<FileInfo> GetFilesToStage(RequestBatchSelection selection)
    {
        var filesToStage = new List<FileInfo>();

        foreach (var selectedFile in selection.SelectedFiles)
        {
            filesToStage.Add(selectedFile);

            var sidecarPath = selectedFile.FullName + ".headers.json";
            if (File.Exists(sidecarPath))
            {
                filesToStage.Add(new FileInfo(sidecarPath));
            }
        }

        return filesToStage;
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
        FileInfo? maskRulesFile,
        string? contentTypeOverride,
        string? soapAction,
        string? requestRange,
        DirectoryInfo? outputDir,
        OutputFormat[] formats,
        HtmlReportMode htmlMode,
        int htmlChunkSize,
        int markdownPageSize,
        bool disableTruncation,
        CancellationToken cancellationToken)
    {
        if (!requestDir.Exists)
        {
            Console.Error.WriteLine($"Request directory not found: {requestDir.FullName}");
            return 1;
        }

        RequestBatchSelection stagingSelection;
        try
        {
            stagingSelection = CreateRequestBatchSelection(requestDir, requestRange);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Request comparison:");
        Console.WriteLine($"  Requests:    {requestDir.FullName}");
        Console.WriteLine($"  Endpoint A:  {endpointA}");
        Console.WriteLine($"  Endpoint B:  {endpointB}");
        Console.WriteLine($"  Model:       {modelName}");
        Console.WriteLine($"  Concurrency: {maxConcurrency}");
        Console.WriteLine($"  Range:       {stagingSelection.AppliedRangeDisplay}");
        Console.WriteLine($"  Selected:    {stagingSelection.SelectedFileCount}/{stagingSelection.TotalEligibleFileCount} file(s)");
        if (!string.IsNullOrWhiteSpace(contentTypeOverride))
        {
            Console.WriteLine($"  Content-Type: {contentTypeOverride}");
        }

        if (!string.IsNullOrWhiteSpace(soapAction))
        {
            Console.WriteLine($"  SOAPAction: {soapAction}");
        }

        Console.WriteLine();

        // Validate model name up-front — fail fast before staging any temp files
        await using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);

        var xmlDeserializationService = serviceProvider.GetRequiredService<IXmlDeserializationService>();
        var availableModels = xmlDeserializationService.GetRegisteredModelNames()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        if (!availableModels.Contains(modelName, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Error: Unknown model name '{modelName}'.");
            Console.Error.WriteLine($"Available models: {string.Join(", ", availableModels)}");
            Console.Error.WriteLine($"Use -m with one of the listed names. If '{modelName}' is a new model, it must be registered in ServiceProviderFactory.");
            return 1;
        }

        // Stage the request files into the temp batch directory that RequestFileParserService expects
        var batchId = await StageRequestBatchAsync(stagingSelection, cancellationToken);

        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var ignoreRulesResult = await LoadIgnoreRulesAsync(ignoreRulesFile, cancellationToken);
        if (!ignoreRulesResult.IsSuccess)
        {
            Console.Error.WriteLine(ignoreRulesResult.ErrorMessage);
            return 1;
        }

        var maskRulesResult = await LoadMaskRulesAsync(maskRulesFile, cancellationToken);
        if (!maskRulesResult.IsSuccess)
        {
            Console.Error.WriteLine(maskRulesResult.ErrorMessage);
            return 1;
        }

        var soapHeaders = string.IsNullOrWhiteSpace(soapAction)
            ? null
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SOAPAction"] = soapAction,
            };

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
            MaskRules = maskRulesResult.MaskRules,
            ContentTypeOverride = contentTypeOverride,
            HeadersA = soapHeaders,
            HeadersB = soapHeaders,
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
            GeneratedAtUtc = DateTime.UtcNow,
            Elapsed = stopwatch.Elapsed,
            CommandName = "request",
            EndpointA = endpointA,
            EndpointB = endpointB,
            ModelName = modelName,
            JobId = job.JobId,
            MostAffectedFields = MostAffectedFieldsAggregator.Build(result),
            HtmlMode = htmlMode,
            HtmlDetailChunkSize = htmlChunkSize,
            MarkdownPageSize = markdownPageSize,
            DisableTruncation = disableTruncation,
        };

        var resolvedOutputDir = outputDir?.FullName ?? Directory.GetCurrentDirectory();
        var outputTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var format in formats.Distinct())
        {
            switch (format)
            {
                case OutputFormat.Console:
                    ConsoleReportWriter.Write(reportContext);
                    break;
                case OutputFormat.Json:
                    var jsonPath = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}.json");
                    await JsonReportWriter.WriteAsync(reportContext, jsonPath);
                    Console.WriteLine($"  JSON report: {jsonPath}");
                    break;
                case OutputFormat.Html:
                    var htmlPath = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}.html");
                    await HtmlReportWriter.WriteAsync(reportContext, htmlPath);
                    Console.WriteLine($"  HTML report: {htmlPath}");
                    break;
                case OutputFormat.Markdown:
                    var mdPath = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}.md");
                    var pageCount = await MarkdownReportWriter.WriteAsync(reportContext, mdPath);
                    var pageSuffix = pageCount > 0 ? $" (+{pageCount} detail pages)" : string.Empty;
                    Console.WriteLine($"  Markdown report: {mdPath}{pageSuffix}");
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
        RequestBatchSelection selection,
        CancellationToken cancellationToken)
    {
        var batchId = Guid.NewGuid().ToString("N")[..12];
        var batchPath = Path.Combine(Path.GetTempPath(), "ComparisonToolRequests", batchId);
        Directory.CreateDirectory(batchPath);

        Console.WriteLine($"  Staging {selection.SelectedFileCount} request file(s)...");

        foreach (var file in GetFilesToStage(selection))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var destPath = Path.Combine(batchPath, file.Name);
            await CopyFileAsync(file.FullName, destPath, cancellationToken);
        }

        return batchId;
    }

    private static RequestOrdinalRange ApplyRequestRange(
        RequestOrdinalRange? requestedRange,
        int totalEligibleFileCount)
    {
        if (requestedRange is null)
        {
            return new RequestOrdinalRange(1, totalEligibleFileCount);
        }

        if (requestedRange.Value.StartOrdinal > totalEligibleFileCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedRange),
                $"Invalid --range value '{requestedRange.Value}'. Range start {requestedRange.Value.StartOrdinal} exceeds the available eligible request file count {totalEligibleFileCount}.");
        }

        return new RequestOrdinalRange(
            requestedRange.Value.StartOrdinal,
            Math.Min(requestedRange.Value.EndOrdinal, totalEligibleFileCount));
    }

    private static List<FileInfo> GetEligibleRequestFiles(DirectoryInfo requestDir)
    {
        return requestDir.GetFiles("*.*", SearchOption.TopDirectoryOnly)
            .Where(IsEligibleRequestFile)
            .OrderBy(file => file.Name, StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsEligibleRequestFile(FileInfo file)
    {
        if (file.Name.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return file.Extension.Equals(".xml", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".json", StringComparison.OrdinalIgnoreCase)
            || file.Extension.Equals(".txt", StringComparison.OrdinalIgnoreCase);
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

            if (string.IsNullOrWhiteSpace(json))
            {
                return IgnoreRulesLoadResult.Success(new List<IgnoreRuleDto>(), new List<SmartIgnoreRuleDto>());
            }

            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var rules = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.ListIgnoreRuleDto) ?? new List<IgnoreRuleDto>();
                return IgnoreRulesLoadResult.Success(rules, new List<SmartIgnoreRuleDto>());
            }

            var container = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.IgnoreRulesContainer) ?? new IgnoreRulesContainer();
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

    internal static async Task<MaskRulesLoadResult> LoadMaskRulesAsync(
        FileInfo? fileInfo,
        CancellationToken cancellationToken)
    {
        if (fileInfo == null)
        {
            return MaskRulesLoadResult.Success(null);
        }

        if (!fileInfo.Exists)
        {
            return MaskRulesLoadResult.Failure($"Mask rules file not found: {fileInfo.FullName}");
        }

        try
        {
            var json = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);

            if (string.IsNullOrWhiteSpace(json))
            {
                return MaskRulesLoadResult.Success(new List<MaskRuleDto>());
            }

            List<MaskRuleDto> rules;
            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                rules = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.ListMaskRuleDto) ?? new List<MaskRuleDto>();
            }
            else
            {
                var container = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.MaskRulesContainer) ?? new MaskRulesContainer();
                rules = container.MaskRules ?? new List<MaskRuleDto>();
            }

            if (rules.Any(rule => rule is null))
            {
                return MaskRulesLoadResult.Failure("Invalid mask rules JSON: maskRules cannot contain null entries.");
            }

            rules = rules
                .Select(rule => string.IsNullOrWhiteSpace(rule.MaskCharacter)
                    ? rule with { MaskCharacter = "*" }
                    : rule)
                .ToList();

            var validationError = ValidateMaskRules(rules);
            if (validationError != null)
            {
                return MaskRulesLoadResult.Failure(validationError);
            }

            return MaskRulesLoadResult.Success(rules);
        }
        catch (JsonException ex)
        {
            return MaskRulesLoadResult.Failure($"Invalid mask rules JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return MaskRulesLoadResult.Failure($"Failed to read mask rules file: {ex.Message}");
        }
    }

    private static string? ValidateMaskRules(IEnumerable<MaskRuleDto> rules)
    {
        foreach (var rule in rules)
        {
            if (rule is null)
            {
                return "Invalid mask rules JSON: maskRules cannot contain null entries.";
            }

            if (string.IsNullOrWhiteSpace(rule.PropertyPath))
            {
                return "Invalid mask rules JSON: each rule must include a non-empty propertyPath.";
            }

            if (rule.PreserveLastCharacters < 0)
            {
                return $"Invalid mask rules JSON: rule '{rule.PropertyPath}' has preserveLastCharacters {rule.PreserveLastCharacters}. Values must be zero or greater.";
            }

            if (string.IsNullOrWhiteSpace(rule.MaskCharacter) || rule.MaskCharacter.Length != 1)
            {
                return $"Invalid mask rules JSON: rule '{rule.PropertyPath}' must specify exactly one character for maskCharacter.";
            }
        }

        return null;
    }

    private sealed class IgnoreRulesContainer
    {
        public List<IgnoreRuleDto>? IgnoreRules { get; init; }

        public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }
    }

    private sealed class MaskRulesContainer
    {
        public List<MaskRuleDto>? MaskRules { get; init; }
    }

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<IgnoreRuleDto>))]
    [JsonSerializable(typeof(List<SmartIgnoreRuleDto>))]
    [JsonSerializable(typeof(List<MaskRuleDto>))]
    [JsonSerializable(typeof(IgnoreRulesContainer))]
    [JsonSerializable(typeof(MaskRulesContainer))]
    private sealed partial class IgnoreRulesJsonContext : JsonSerializerContext
    {
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

    internal sealed record MaskRulesLoadResult
    {
        public bool IsSuccess { get; init; }

        public string? ErrorMessage { get; init; }

        public List<MaskRuleDto>? MaskRules { get; init; }

        public static MaskRulesLoadResult Success(List<MaskRuleDto>? maskRules)
            => new MaskRulesLoadResult
            {
                IsSuccess = true,
                MaskRules = maskRules,
            };

        public static MaskRulesLoadResult Failure(string message)
            => new MaskRulesLoadResult
            {
                IsSuccess = false,
                ErrorMessage = message,
            };
    }

    internal readonly struct RequestOrdinalRange : IEquatable<RequestOrdinalRange>
    {
        public RequestOrdinalRange(int startOrdinal, int endOrdinal)
        {
            StartOrdinal = startOrdinal;
            EndOrdinal = endOrdinal;
        }

        public int StartOrdinal { get; }

        public int EndOrdinal { get; }

        public bool Equals(RequestOrdinalRange other) => StartOrdinal == other.StartOrdinal && EndOrdinal == other.EndOrdinal;

        public override bool Equals(object? obj) => obj is RequestOrdinalRange other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(StartOrdinal, EndOrdinal);

        public override string ToString() => $"{StartOrdinal}-{EndOrdinal}";
    }

    internal sealed class RequestBatchSelection
    {
        public RequestBatchSelection(
            int totalEligibleFileCount,
            IReadOnlyList<FileInfo> selectedFiles,
            RequestOrdinalRange? requestedRange,
            RequestOrdinalRange appliedRange)
        {
            TotalEligibleFileCount = totalEligibleFileCount;
            SelectedFiles = selectedFiles;
            RequestedRange = requestedRange;
            AppliedRange = appliedRange;
        }

        public int TotalEligibleFileCount { get; }

        public IReadOnlyList<FileInfo> SelectedFiles { get; }

        public RequestOrdinalRange? RequestedRange { get; }

        public RequestOrdinalRange AppliedRange { get; }

        public int SelectedFileCount => SelectedFiles.Count;

        public string AppliedRangeDisplay => RequestedRange is null
            ? $"all ({AppliedRange})"
            : RequestedRange.Value.Equals(AppliedRange)
                ? AppliedRange.ToString()
                : $"{AppliedRange} (requested {RequestedRange.Value})";
    }
}
