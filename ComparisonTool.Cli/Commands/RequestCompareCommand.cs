using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using ComparisonTool.Cli.Infrastructure;
using ComparisonTool.Cli.Reporting;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.RequestComparison.AlternateContracts;
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

        var endpointAOption = new Option<string?>("--endpoint-a", "-a")
        {
            Description = "URL or configured endpoint name for endpoint A",
        };

        var endpointBOption = new Option<string?>("--endpoint-b", "-b")
        {
            Description = "URL or configured endpoint name for endpoint B",
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

        var treatNullEmptyCollectionsOption = new Option<bool>("--treat-null-empty-collections-equal")
        {
            Description = "Treat null and empty collections as equivalent during comparison",
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
            Description = "Path to JSON file containing IgnoreRule definitions",
        };

        var maskRulesFileOption = new Option<FileInfo?>("--mask-rules")
        {
            Description = "Path to JSON file containing MaskRuleDto definitions for response masking",
        };

        var contentTypeOption = new Option<string?>("--content-type")
        {
            Description = "Override Content-Type header for endpoint A request bodies; endpoint B uses profile content type in alternate-contract mode",
        };

        var soapActionOption = new Option<string?>("--soap-action")
        {
            Description = "Optional SOAPAction header value to send with every request",
        };

        var alternateContractOption = new Option<bool>("--alternate-contract")
        {
            Description = "Enable alternate request/response contract processing for endpoint B",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var alternateContractProfileOption = new Option<string?>("--alternate-contract-profile")
        {
            Description = "Alternate contract profile id. Implies --alternate-contract.",
        };

        var useProfileEndpointsOption = new Option<bool>("--use-profile-endpoints")
        {
            Description = "Use the selected alternate contract profile's suggested configured endpoints when endpoint A/B are omitted",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
        };

        var headerOption = CreateHeaderOption("--header", "Header applied to both endpoints. Repeatable. Format: 'Name: Value'.");
        var headerAOption = CreateHeaderOption("--header-a", "Header applied only to endpoint A. Repeatable. Format: 'Name: Value'.");
        var headerBOption = CreateHeaderOption("--header-b", "Header applied only to endpoint B. Repeatable. Format: 'Name: Value'.");

        var headersFileOption = new Option<FileInfo?>("--headers-file")
        {
            Description = "JSON header map applied to both endpoints",
        };

        var headersAFileOption = new Option<FileInfo?>("--headers-a-file")
        {
            Description = "JSON header map applied only to endpoint A",
        };

        var headersBFileOption = new Option<FileInfo?>("--headers-b-file")
        {
            Description = "JSON header map applied only to endpoint B",
        };

        var noEndpointDefaultsOption = new Option<bool>("--no-endpoint-defaults")
        {
            Description = "Do not apply ContentType or DefaultHeaders from configured endpoint options",
            Arity = ArgumentArity.ZeroOrOne,
            DefaultValueFactory = _ => false,
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
            treatNullEmptyCollectionsOption,
            ignoreNamespacesOption,
            semanticAnalysisOption,
            ignoreRulesFileOption,
            maskRulesFileOption,
            contentTypeOption,
            soapActionOption,
            alternateContractOption,
            alternateContractProfileOption,
            useProfileEndpointsOption,
            headerOption,
            headerAOption,
            headerBOption,
            headersFileOption,
            headersAFileOption,
            headersBFileOption,
            noEndpointDefaultsOption,
            rangeOption,
            outputOption,
            formatOption,
            pageSizeOption,
            disableTruncationOption,
        };

        command.SetAction(async (parseResult, cancellationToken) =>
        {
            var requestDir = parseResult.GetValue(requestDirArg)
                ?? throw new InvalidOperationException("Missing required argument request-directory.");
            var model = parseResult.GetValue(modelOption)
                ?? throw new InvalidOperationException("Missing required option --model.");

            var options = new RequestCompareCliOptions
            {
                RequestDirectory = requestDir,
                EndpointA = parseResult.GetValue(endpointAOption),
                EndpointB = parseResult.GetValue(endpointBOption),
                ModelName = model,
                MaxConcurrency = parseResult.GetValue(concurrencyOption),
                TimeoutMs = parseResult.GetValue(timeoutOption),
                IgnoreCollectionOrder = parseResult.GetValue(ignoreCollectionOrderOption),
                IgnoreStringCase = parseResult.GetValue(ignoreCaseOption),
                IgnoreTrailingWhitespaceAtEnd = parseResult.GetValue(ignoreTrailingWhitespaceOption),
                TreatNullAndEmptyCollectionsAsEqual = parseResult.GetValue(treatNullEmptyCollectionsOption),
                IgnoreXmlNamespaces = parseResult.GetValue(ignoreNamespacesOption),
                EnableSemanticAnalysis = parseResult.GetValue(semanticAnalysisOption),
                IgnoreRulesFile = parseResult.GetValue(ignoreRulesFileOption),
                MaskRulesFile = parseResult.GetValue(maskRulesFileOption),
                ContentTypeOverride = parseResult.GetValue(contentTypeOption),
                SoapAction = parseResult.GetValue(soapActionOption),
                UseAlternateContract = parseResult.GetValue(alternateContractOption),
                AlternateContractProfileId = parseResult.GetValue(alternateContractProfileOption),
                UseProfileEndpoints = parseResult.GetValue(useProfileEndpointsOption),
                Headers = parseResult.GetValue(headerOption) ?? Array.Empty<string>(),
                HeadersA = parseResult.GetValue(headerAOption) ?? Array.Empty<string>(),
                HeadersB = parseResult.GetValue(headerBOption) ?? Array.Empty<string>(),
                HeadersFile = parseResult.GetValue(headersFileOption),
                HeadersAFile = parseResult.GetValue(headersAFileOption),
                HeadersBFile = parseResult.GetValue(headersBFileOption),
                NoEndpointDefaults = parseResult.GetValue(noEndpointDefaultsOption),
                RequestRange = parseResult.GetValue(rangeOption),
                OutputDirectory = parseResult.GetValue(outputOption),
                Formats = parseResult.GetValue(formatOption) ?? new[] { OutputFormat.Console },
                MarkdownPageSize = parseResult.GetValue(pageSizeOption),
                DisableTruncation = parseResult.GetValue(disableTruncationOption),
            };

            return await ExecuteAsync(configuration, options, cancellationToken).ConfigureAwait(false);
        });

        return command;
    }

    /// <summary>
    /// Creates the root-level model discovery command.
    /// </summary>
    public static Command CreateModelsCommand(IConfiguration configuration)
    {
        var command = new Command("request-models", "List request comparison model names registered in the CLI host");
        command.SetAction(_ =>
        {
            using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);
            var deserializationService = serviceProvider.GetRequiredService<IDeserializationService>();
            var models = deserializationService.GetRegisteredModelNames()
                .OrderBy(model => model, StringComparer.Ordinal)
                .ToList();

            foreach (var model in models)
            {
                Console.WriteLine(model);
            }

            return models.Count == 0 ? 1 : 0;
        });

        return command;
    }

    /// <summary>
    /// Creates the root-level alternate-contract profile discovery command.
    /// </summary>
    public static Command CreateProfilesCommand(IConfiguration configuration)
    {
        var modelOption = new Option<string?>("--model", "-m")
        {
            Description = "Filter profiles to a model name",
        };

        var command = new Command("request-profiles", "List alternate contract profiles registered in the CLI host")
        {
            modelOption,
        };

        command.SetAction(parseResult =>
        {
            using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);
            var registry = serviceProvider.GetRequiredService<IRequestComparisonAlternateContractProfileRegistry>();
            var deserializationService = serviceProvider.GetRequiredService<IDeserializationService>();
            var modelFilter = parseResult.GetValue(modelOption);

            var models = string.IsNullOrWhiteSpace(modelFilter)
                ? deserializationService.GetRegisteredModelNames().OrderBy(model => model, StringComparer.Ordinal).ToList()
                : new List<string> { modelFilter };

            var printed = 0;
            foreach (var model in models)
            {
                var profileIds = registry.GetProfileIds(model);
                foreach (var profileId in profileIds)
                {
                    Console.WriteLine($"{model}: {profileId}");
                    printed++;
                }
            }

            if (printed == 0)
            {
                Console.WriteLine(string.IsNullOrWhiteSpace(modelFilter)
                    ? "No alternate contract profiles are registered."
                    : $"No alternate contract profiles are registered for {modelFilter}.");
            }

            return 0;
        });

        return command;
    }

    /// <summary>
    /// Creates the root-level endpoint discovery command.
    /// </summary>
    public static Command CreateEndpointsCommand(IConfiguration configuration)
    {
        var command = new Command("request-endpoints", "List configured request comparison endpoints");
        command.SetAction(_ =>
        {
            var endpointOptions = LoadEndpointOptions(configuration);
            if (endpointOptions.Endpoints.Count == 0)
            {
                Console.WriteLine("No request comparison endpoints are configured.");
                return 0;
            }

            foreach (var endpoint in endpointOptions.Endpoints)
            {
                Console.WriteLine($"{endpoint.Name}: {endpoint.Url}");
                if (!string.IsNullOrWhiteSpace(endpoint.ContentType))
                {
                    Console.WriteLine($"  Content-Type: {endpoint.ContentType}");
                }

                if (endpoint.DefaultHeaders is { Count: > 0 })
                {
                    Console.WriteLine($"  Default headers: {string.Join(", ", endpoint.DefaultHeaders.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))}");
                }
            }

            return 0;
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
            requestDir,
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

    internal static bool TryParseHeader(string headerText, out KeyValuePair<string, string> header, out string? errorMessage)
    {
        header = default;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(headerText))
        {
            errorMessage = "Header value cannot be empty.";
            return false;
        }

        var separatorIndex = headerText.IndexOf(':', StringComparison.Ordinal);
        if (separatorIndex <= 0)
        {
            errorMessage = $"Invalid header '{headerText}'. Expected format 'Name: Value'.";
            return false;
        }

        var name = headerText[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            errorMessage = $"Invalid header '{headerText}'. Header name cannot be empty.";
            return false;
        }

        header = new KeyValuePair<string, string>(name, headerText[(separatorIndex + 1)..].Trim());
        return true;
    }

    internal static RequestComparisonEndpointOptions LoadEndpointOptions(IConfiguration configuration)
    {
        var options = new RequestComparisonEndpointOptions();
        configuration.GetSection("RequestComparison:EndpointOptions").Bind(options);
        options.Endpoints ??= new List<RequestComparisonEndpointOption>();
        return options;
    }

    internal static EndpointResolutionResult ResolveEndpointReference(
        string? reference,
        IReadOnlyList<RequestComparisonEndpointOption> configuredEndpoints,
        string optionName)
    {
        if (string.IsNullOrWhiteSpace(reference))
        {
            return EndpointResolutionResult.Failure($"{optionName} is required unless --use-profile-endpoints can resolve it from the selected profile.");
        }

        var trimmed = reference.Trim();
        var configuredMatch = configuredEndpoints.FirstOrDefault(endpoint =>
            string.Equals(endpoint.Name, trimmed, StringComparison.OrdinalIgnoreCase)
            || string.Equals(endpoint.Url, trimmed, StringComparison.OrdinalIgnoreCase));

        if (configuredMatch != null)
        {
            return EndpointResolutionResult.Success(configuredMatch.Url, configuredMatch);
        }

        if (Uri.TryCreate(trimmed, UriKind.Absolute, out _))
        {
            return EndpointResolutionResult.Success(trimmed, null);
        }

        return EndpointResolutionResult.Failure(
            $"{optionName} value '{reference}' is not an absolute URL or configured endpoint name.");
    }

    internal static bool ShouldUseAlternateContract(RequestCompareCliOptions options)
    {
        return options.UseAlternateContract || !string.IsNullOrWhiteSpace(options.AlternateContractProfileId);
    }

    internal static AlternateContractProfileResolutionResult ResolveAlternateContractProfile(
        RequestCompareCliOptions options,
        IRequestComparisonAlternateContractProfileRegistry registry)
    {
        if (!ShouldUseAlternateContract(options))
        {
            return AlternateContractProfileResolutionResult.Success(null);
        }

        if (registry.TryResolve(options.ModelName, options.AlternateContractProfileId, out var profile, out var errorMessage))
        {
            return AlternateContractProfileResolutionResult.Success(profile);
        }

        return AlternateContractProfileResolutionResult.Failure(
            errorMessage ?? "Alternate contract profile could not be resolved.",
            registry.GetProfileIds(options.ModelName));
    }
    internal static async Task<HeaderBuildResult> BuildHeadersAsync(
        RequestCompareCliOptions options,
        RequestComparisonEndpointOption? endpointA,
        RequestComparisonEndpointOption? endpointB,
        CancellationToken cancellationToken)
    {
        var headersA = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var headersB = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (!options.NoEndpointDefaults)
        {
            ApplyHeaders(headersA, endpointA?.DefaultHeaders);
            ApplyHeaders(headersB, endpointB?.DefaultHeaders);
        }

        var commonFileHeaders = await LoadHeaderFileAsync(options.HeadersFile, cancellationToken).ConfigureAwait(false);
        if (!commonFileHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(commonFileHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersA, commonFileHeaders.Headers);
        ApplyHeaders(headersB, commonFileHeaders.Headers);

        var commonInlineHeaders = ParseHeaderArguments(options.Headers);
        if (!commonInlineHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(commonInlineHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersA, commonInlineHeaders.Headers);
        ApplyHeaders(headersB, commonInlineHeaders.Headers);

        if (!string.IsNullOrWhiteSpace(options.SoapAction))
        {
            headersA["SOAPAction"] = options.SoapAction;
            headersB["SOAPAction"] = options.SoapAction;
        }

        var endpointAFileHeaders = await LoadHeaderFileAsync(options.HeadersAFile, cancellationToken).ConfigureAwait(false);
        if (!endpointAFileHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(endpointAFileHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersA, endpointAFileHeaders.Headers);

        var endpointBFileHeaders = await LoadHeaderFileAsync(options.HeadersBFile, cancellationToken).ConfigureAwait(false);
        if (!endpointBFileHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(endpointBFileHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersB, endpointBFileHeaders.Headers);

        var endpointAInlineHeaders = ParseHeaderArguments(options.HeadersA);
        if (!endpointAInlineHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(endpointAInlineHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersA, endpointAInlineHeaders.Headers);

        var endpointBInlineHeaders = ParseHeaderArguments(options.HeadersB);
        if (!endpointBInlineHeaders.IsSuccess)
        {
            return HeaderBuildResult.Failure(endpointBInlineHeaders.ErrorMessage!);
        }

        ApplyHeaders(headersB, endpointBInlineHeaders.Headers);

        return HeaderBuildResult.Success(headersA, headersB);
    }

    private static Option<string[]> CreateHeaderOption(string name, string description) => new(name)
    {
        Description = description,
        Arity = ArgumentArity.ZeroOrMore,
        AllowMultipleArgumentsPerToken = false,
        DefaultValueFactory = _ => Array.Empty<string>(),
    };

    private static async Task<int> ExecuteAsync(
        IConfiguration configuration,
        RequestCompareCliOptions options,
        CancellationToken cancellationToken)
    {
        if (!options.RequestDirectory.Exists)
        {
            Console.Error.WriteLine($"Request directory not found: {options.RequestDirectory.FullName}");
            return 1;
        }

        await using var serviceProvider = ServiceProviderFactory.CreateServiceProvider(configuration);
        var deserializationService = serviceProvider.GetRequiredService<IDeserializationService>();
        var availableModels = deserializationService.GetRegisteredModelNames()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        if (!availableModels.Contains(options.ModelName, StringComparer.Ordinal))
        {
            Console.Error.WriteLine($"Error: Unknown model name '{options.ModelName}'.");
            Console.Error.WriteLine($"Available models: {string.Join(", ", availableModels)}");
            Console.Error.WriteLine($"Use -m with one of the listed names. If '{options.ModelName}' is a new model, it must be registered in ServiceProviderFactory.");
            return 1;
        }

        var endpointOptions = LoadEndpointOptions(configuration);
        var useAlternateContract = ShouldUseAlternateContract(options);
        RequestComparisonAlternateContractProfile? alternateProfile = null;

        if (options.UseProfileEndpoints && !useAlternateContract)
        {
            Console.Error.WriteLine("Error: --use-profile-endpoints requires --alternate-contract or --alternate-contract-profile.");
            return 1;
        }

        if (useAlternateContract)
        {
            var registry = serviceProvider.GetRequiredService<IRequestComparisonAlternateContractProfileRegistry>();
            var profileResult = ResolveAlternateContractProfile(options, registry);
            if (!profileResult.IsSuccess)
            {
                Console.Error.WriteLine($"Error: {profileResult.ErrorMessage}");
                if (profileResult.AvailableProfileIds.Count > 0)
                {
                    Console.Error.WriteLine($"Available profiles for {options.ModelName}: {string.Join(", ", profileResult.AvailableProfileIds)}");
                }

                return 1;
            }

            alternateProfile = profileResult.Profile;
        }

        var endpointAReference = options.EndpointA;
        var endpointBReference = options.EndpointB;
        if (options.UseProfileEndpoints)
        {
            endpointAReference ??= ResolveSuggestedEndpointReference(alternateProfile!.SuggestEndpointA, endpointOptions.Endpoints);
            endpointBReference ??= ResolveSuggestedEndpointReference(alternateProfile!.SuggestEndpointB, endpointOptions.Endpoints);
        }

        var endpointAResult = ResolveEndpointReference(endpointAReference, endpointOptions.Endpoints, "Endpoint A");
        if (!endpointAResult.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {endpointAResult.ErrorMessage}");
            return 1;
        }

        var endpointBResult = ResolveEndpointReference(endpointBReference, endpointOptions.Endpoints, "Endpoint B");
        if (!endpointBResult.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {endpointBResult.ErrorMessage}");
            return 1;
        }

        var contentTypeOverride = !string.IsNullOrWhiteSpace(options.ContentTypeOverride)
            ? options.ContentTypeOverride
            : !options.NoEndpointDefaults && !string.IsNullOrWhiteSpace(endpointAResult.EndpointOption?.ContentType)
                ? endpointAResult.EndpointOption.ContentType
                : null;

        var headersResult = await BuildHeadersAsync(
            options,
            endpointAResult.EndpointOption,
            endpointBResult.EndpointOption,
            cancellationToken).ConfigureAwait(false);
        if (!headersResult.IsSuccess)
        {
            Console.Error.WriteLine($"Error: {headersResult.ErrorMessage}");
            return 1;
        }

        var ignoreRulesResult = await LoadIgnoreRulesAsync(options.IgnoreRulesFile, cancellationToken).ConfigureAwait(false);
        if (!ignoreRulesResult.IsSuccess)
        {
            Console.Error.WriteLine(ignoreRulesResult.ErrorMessage);
            return 1;
        }

        var maskRulesResult = await LoadMaskRulesAsync(options.MaskRulesFile, cancellationToken).ConfigureAwait(false);
        if (!maskRulesResult.IsSuccess)
        {
            Console.Error.WriteLine(maskRulesResult.ErrorMessage);
            return 1;
        }

        RequestBatchSelection stagingSelection;
        try
        {
            stagingSelection = CreateRequestBatchSelection(options.RequestDirectory, options.RequestRange);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        Console.WriteLine("Request comparison:");
        Console.WriteLine($"  Requests:    {options.RequestDirectory.FullName}");
        Console.WriteLine($"  Endpoint A:  {endpointAResult.Url}{FormatEndpointName(endpointAResult.EndpointOption)}");
        Console.WriteLine($"  Endpoint B:  {endpointBResult.Url}{FormatEndpointName(endpointBResult.EndpointOption)}");
        Console.WriteLine($"  Model:       {options.ModelName}");
        Console.WriteLine($"  Concurrency: {options.MaxConcurrency}");
        Console.WriteLine($"  Range:       {stagingSelection.AppliedRangeDisplay}");
        Console.WriteLine($"  Selected:    {stagingSelection.SelectedFileCount}/{stagingSelection.TotalEligibleFileCount} file(s)");
        if (!string.IsNullOrWhiteSpace(contentTypeOverride))
        {
            Console.WriteLine($"  Content-Type: {contentTypeOverride}");
        }

        if (useAlternateContract && alternateProfile != null)
        {
            Console.WriteLine($"  Alternate contract: {alternateProfile.ProfileId}");
            Console.WriteLine($"  Endpoint B request Content-Type: {alternateProfile.AlternateRequestContentType}");
        }

        PrintHeaderSummary("Headers A", headersResult.HeadersA);
        PrintHeaderSummary("Headers B", headersResult.HeadersB);
        Console.WriteLine();

        var batchId = await StageRequestBatchAsync(stagingSelection, cancellationToken).ConfigureAwait(false);
        var jobService = serviceProvider.GetRequiredService<RequestComparisonJobService>();

        var createRequest = new CreateRequestComparisonJobRequest
        {
            RequestBatchId = batchId,
            EndpointA = endpointAResult.Url,
            EndpointALabel = endpointAResult.Label,
            EndpointB = endpointBResult.Url,
            EndpointBLabel = endpointBResult.Label,
            TimeoutMs = options.TimeoutMs,
            MaxConcurrency = options.MaxConcurrency,
            ModelName = options.ModelName,
            UseAlternateContractForEndpointB = useAlternateContract,
            AlternateContractProfileId = alternateProfile?.ProfileId ?? options.AlternateContractProfileId,
            IgnoreCollectionOrder = options.IgnoreCollectionOrder,
            IgnoreStringCase = options.IgnoreStringCase,
            IgnoreTrailingWhitespaceAtEnd = options.IgnoreTrailingWhitespaceAtEnd,
            TreatNullAndEmptyCollectionsAsEqual = options.TreatNullAndEmptyCollectionsAsEqual,
            IgnoreXmlNamespaces = options.IgnoreXmlNamespaces,
            EnableSemanticAnalysis = options.EnableSemanticAnalysis,
            IgnoreRules = ignoreRulesResult.IgnoreRules,
            SmartIgnoreRules = ignoreRulesResult.SmartIgnoreRules,
            MaskRules = maskRulesResult.MaskRules,
            ContentTypeOverride = contentTypeOverride,
            HeadersA = headersResult.HeadersA,
            HeadersB = headersResult.HeadersB,
        };

        RequestComparisonJob job;
        try
        {
            job = jobService.CreateJob(createRequest);
        }
        catch (InvalidOperationException ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }

        var progress = new Progress<(int Completed, int Total, string Message)>(p =>
        {
            // Progress updates are handled by ConsoleProgressPublisher via IComparisonProgressPublisher.
        });

        var stopwatch = Stopwatch.StartNew();

        try
        {
            await jobService.ExecuteJobAsync(job.JobId, progress, cancellationToken).ConfigureAwait(false);
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
            EndpointA = endpointAResult.Url,
            EndpointALabel = endpointAResult.Label,
            EndpointB = endpointBResult.Url,
            EndpointBLabel = endpointBResult.Label,
            ModelName = options.ModelName,
            JobId = job.JobId,
            MostAffectedFields = MostAffectedFieldsAggregator.Build(result),
            MarkdownPageSize = options.MarkdownPageSize,
            DisableTruncation = options.DisableTruncation,
        };

        var resolvedOutputDir = options.OutputDirectory?.FullName ?? Directory.GetCurrentDirectory();
        var outputTimestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        Directory.CreateDirectory(resolvedOutputDir);

        foreach (var format in options.Formats.Distinct())
        {
            switch (format)
            {
                case OutputFormat.Console:
                    ConsoleReportWriter.Write(reportContext);
                    break;
                case OutputFormat.Json:
                    var jsonPath = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}.json");
                    await JsonReportWriter.WriteAsync(reportContext, jsonPath).ConfigureAwait(false);
                    Console.WriteLine($"  JSON report: {jsonPath}");
                    break;
                case OutputFormat.Html:
                    var blazorDir = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}");
                    var htmlPath = await BlazorReportWriter.WriteAsync(reportContext, blazorDir).ConfigureAwait(false);
                    Console.WriteLine($"  HTML report: {htmlPath}");
                    Console.WriteLine($"  Local view:  run {Path.Combine(blazorDir, "serve.cmd")}");
                    break;
                case OutputFormat.Markdown:
                    var mdPath = Path.Combine(resolvedOutputDir, $"request-comparison-{outputTimestamp}.md");
                    var pageCount = await MarkdownReportWriter.WriteAsync(reportContext, mdPath).ConfigureAwait(false);
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
            var relativePath = NormalizeRelativePath(Path.GetRelativePath(selection.RequestDirectory.FullName, file.FullName));
            var destPath = Path.Combine(batchPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destPath) ?? batchPath);
            await CopyFileAsync(file.FullName, destPath, cancellationToken).ConfigureAwait(false);
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
        return requestDir.GetFiles("*.*", SearchOption.AllDirectories)
            .Where(IsEligibleRequestFile)
            .OrderBy(file => NormalizeRelativePath(Path.GetRelativePath(requestDir.FullName, file.FullName)), StringComparer.Ordinal)
            .ToList();
    }

    private static bool IsEligibleRequestFile(FileInfo file)
    {
        if (file.Name.EndsWith(".headers.json", StringComparison.OrdinalIgnoreCase)
            || file.Name.StartsWith("_", StringComparison.Ordinal))
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
        await sourceStream.CopyToAsync(destStream, bufferSize, cancellationToken).ConfigureAwait(false);
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
            var json = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(json))
            {
                return IgnoreRulesLoadResult.Success(new List<IgnoreRule>(), new List<SmartIgnoreRuleDto>());
            }

            if (json.TrimStart().StartsWith("[", StringComparison.Ordinal))
            {
                var rules = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.ListIgnoreRule) ?? new List<IgnoreRule>();
                return IgnoreRulesLoadResult.Success(rules, new List<SmartIgnoreRuleDto>());
            }

            var container = JsonSerializer.Deserialize(json, IgnoreRulesJsonContext.Default.IgnoreRulesContainer) ?? new IgnoreRulesContainer();
            return IgnoreRulesLoadResult.Success(
                container.IgnoreRules ?? new List<IgnoreRule>(),
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
            var json = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken).ConfigureAwait(false);

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

    private static HeaderFileLoadResult ParseHeaderArguments(IEnumerable<string> headerTexts)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var headerText in headerTexts)
        {
            if (!TryParseHeader(headerText, out var header, out var errorMessage))
            {
                return HeaderFileLoadResult.Failure(errorMessage!);
            }

            headers[header.Key] = header.Value;
        }

        return HeaderFileLoadResult.Success(headers);
    }

    private static async Task<HeaderFileLoadResult> LoadHeaderFileAsync(
        FileInfo? fileInfo,
        CancellationToken cancellationToken)
    {
        if (fileInfo == null)
        {
            return HeaderFileLoadResult.Success(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
        }

        if (!fileInfo.Exists)
        {
            return HeaderFileLoadResult.Failure($"Header file not found: {fileInfo.FullName}");
        }

        try
        {
            await using var stream = File.OpenRead(fileInfo.FullName);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return HeaderFileLoadResult.Failure($"Invalid header file JSON '{fileInfo.FullName}': root value must be an object.");
            }

            var headerElement = root.TryGetProperty("headers", out var nestedHeaders)
                ? nestedHeaders
                : root;

            if (headerElement.ValueKind != JsonValueKind.Object)
            {
                return HeaderFileLoadResult.Failure($"Invalid header file JSON '{fileInfo.FullName}': headers value must be an object.");
            }

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var property in headerElement.EnumerateObject())
            {
                if (string.IsNullOrWhiteSpace(property.Name))
                {
                    return HeaderFileLoadResult.Failure($"Invalid header file JSON '{fileInfo.FullName}': header names cannot be empty.");
                }

                headers[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString() ?? string.Empty
                    : property.Value.GetRawText();
            }

            return HeaderFileLoadResult.Success(headers);
        }
        catch (JsonException ex)
        {
            return HeaderFileLoadResult.Failure($"Invalid header file JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            return HeaderFileLoadResult.Failure($"Failed to read header file: {ex.Message}");
        }
    }

    private static void ApplyHeaders(
        IDictionary<string, string> target,
        IReadOnlyDictionary<string, string>? source)
    {
        if (source == null)
        {
            return;
        }

        foreach (var header in source)
        {
            target[header.Key] = header.Value;
        }
    }

    private static string? ResolveSuggestedEndpointReference(
        string? suggestion,
        IReadOnlyList<RequestComparisonEndpointOption> configuredEndpoints)
    {
        if (string.IsNullOrWhiteSpace(suggestion))
        {
            return null;
        }

        var match = configuredEndpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Url, suggestion, StringComparison.OrdinalIgnoreCase))
            ?? configuredEndpoints.FirstOrDefault(endpoint => string.Equals(endpoint.Name, suggestion, StringComparison.OrdinalIgnoreCase))
            ?? configuredEndpoints.FirstOrDefault(endpoint => endpoint.Url.Contains(suggestion, StringComparison.OrdinalIgnoreCase))
            ?? configuredEndpoints.FirstOrDefault(endpoint => endpoint.Name.Contains(suggestion, StringComparison.OrdinalIgnoreCase));

        return match?.Url ?? suggestion;
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

    private static void PrintHeaderSummary(string label, IReadOnlyDictionary<string, string> headers)
    {
        if (headers.Count == 0)
        {
            return;
        }

        Console.WriteLine($"  {label}: {headers.Count} ({string.Join(", ", headers.Keys.OrderBy(key => key, StringComparer.OrdinalIgnoreCase))})");
    }

    private static string BuildEndpointLabel(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            var path = uri.AbsolutePath.Trim('/');
            return string.IsNullOrWhiteSpace(path)
                ? uri.Host
                : $"{uri.Host}/{path}";
        }

        return url;
    }

    private static string FormatEndpointName(RequestComparisonEndpointOption? endpointOption) => endpointOption == null
        ? string.Empty
        : $" ({endpointOption.Name})";

    private static string NormalizeRelativePath(string relativePath) => relativePath
        .Replace('\\', '/')
        .TrimStart('/');

    private sealed class IgnoreRulesContainer
    {
        public List<IgnoreRule>? IgnoreRules { get; init; }

        public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }
    }

    private sealed class MaskRulesContainer
    {
        public List<MaskRuleDto>? MaskRules { get; init; }
    }

    [JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    [JsonSerializable(typeof(List<IgnoreRule>))]
    [JsonSerializable(typeof(List<SmartIgnoreRuleDto>))]
    [JsonSerializable(typeof(List<MaskRuleDto>))]
    [JsonSerializable(typeof(IgnoreRulesContainer))]
    [JsonSerializable(typeof(MaskRulesContainer))]
    private sealed partial class IgnoreRulesJsonContext : JsonSerializerContext
    {
    }

    internal sealed record RequestCompareCliOptions
    {
        public required DirectoryInfo RequestDirectory { get; init; }

        public string? EndpointA { get; init; }

        public string? EndpointB { get; init; }

        public required string ModelName { get; init; }

        public int MaxConcurrency { get; init; }

        public int TimeoutMs { get; init; }

        public bool IgnoreCollectionOrder { get; init; }

        public bool IgnoreStringCase { get; init; }

        public bool IgnoreTrailingWhitespaceAtEnd { get; init; }

        public bool TreatNullAndEmptyCollectionsAsEqual { get; init; }

        public bool IgnoreXmlNamespaces { get; init; }

        public bool EnableSemanticAnalysis { get; init; }

        public FileInfo? IgnoreRulesFile { get; init; }

        public FileInfo? MaskRulesFile { get; init; }

        public string? ContentTypeOverride { get; init; }

        public string? SoapAction { get; init; }

        public bool UseAlternateContract { get; init; }

        public string? AlternateContractProfileId { get; init; }

        public bool UseProfileEndpoints { get; init; }

        public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> HeadersA { get; init; } = Array.Empty<string>();

        public IReadOnlyList<string> HeadersB { get; init; } = Array.Empty<string>();

        public FileInfo? HeadersFile { get; init; }

        public FileInfo? HeadersAFile { get; init; }

        public FileInfo? HeadersBFile { get; init; }

        public bool NoEndpointDefaults { get; init; }

        public string? RequestRange { get; init; }

        public DirectoryInfo? OutputDirectory { get; init; }

        public OutputFormat[] Formats { get; init; } = Array.Empty<OutputFormat>();

        public int MarkdownPageSize { get; init; }

        public bool DisableTruncation { get; init; }
    }

    internal sealed record EndpointResolutionResult
    {
        public bool IsSuccess { get; init; }

        public string Url { get; init; } = string.Empty;

        public RequestComparisonEndpointOption? EndpointOption { get; init; }

        public string Label { get; init; } = string.Empty;

        public string? ErrorMessage { get; init; }

        public static EndpointResolutionResult Success(string url, RequestComparisonEndpointOption? endpointOption) => new()
        {
            IsSuccess = true,
            Url = url,
            EndpointOption = endpointOption,
            Label = endpointOption?.Name ?? BuildEndpointLabel(url),
        };

        public static EndpointResolutionResult Failure(string errorMessage) => new()
        {
            IsSuccess = false,
            ErrorMessage = errorMessage,
        };
    }

    private sealed record HeaderFileLoadResult
    {
        public bool IsSuccess { get; init; }

        public string? ErrorMessage { get; init; }

        public Dictionary<string, string> Headers { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public static HeaderFileLoadResult Success(Dictionary<string, string> headers) => new()
        {
            IsSuccess = true,
            Headers = headers,
        };

        public static HeaderFileLoadResult Failure(string message) => new()
        {
            IsSuccess = false,
            ErrorMessage = message,
        };
    }

    internal sealed record AlternateContractProfileResolutionResult
    {
        public bool IsSuccess { get; init; }

        public RequestComparisonAlternateContractProfile? Profile { get; init; }

        public string? ErrorMessage { get; init; }

        public IReadOnlyList<string> AvailableProfileIds { get; init; } = Array.Empty<string>();

        public static AlternateContractProfileResolutionResult Success(RequestComparisonAlternateContractProfile? profile) => new()
        {
            IsSuccess = true,
            Profile = profile,
        };

        public static AlternateContractProfileResolutionResult Failure(
            string message,
            IReadOnlyList<string> availableProfileIds) => new()
        {
            IsSuccess = false,
            ErrorMessage = message,
            AvailableProfileIds = availableProfileIds,
        };
    }
    internal sealed record HeaderBuildResult
    {
        public bool IsSuccess { get; init; }

        public string? ErrorMessage { get; init; }

        public Dictionary<string, string> HeadersA { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> HeadersB { get; init; } = new(StringComparer.OrdinalIgnoreCase);

        public static HeaderBuildResult Success(
            Dictionary<string, string> headersA,
            Dictionary<string, string> headersB) => new()
        {
            IsSuccess = true,
            HeadersA = headersA,
            HeadersB = headersB,
        };

        public static HeaderBuildResult Failure(string message) => new()
        {
            IsSuccess = false,
            ErrorMessage = message,
        };
    }

    private sealed record IgnoreRulesLoadResult
    {
        public bool IsSuccess { get; init; }

        public string? ErrorMessage { get; init; }

        public List<IgnoreRule>? IgnoreRules { get; init; }

        public List<SmartIgnoreRuleDto>? SmartIgnoreRules { get; init; }

        public static IgnoreRulesLoadResult Success(
            List<IgnoreRule>? ignoreRules,
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
            DirectoryInfo requestDirectory,
            int totalEligibleFileCount,
            IReadOnlyList<FileInfo> selectedFiles,
            RequestOrdinalRange? requestedRange,
            RequestOrdinalRange appliedRange)
        {
            RequestDirectory = requestDirectory;
            TotalEligibleFileCount = totalEligibleFileCount;
            SelectedFiles = selectedFiles;
            RequestedRange = requestedRange;
            AppliedRange = appliedRange;
        }

        public DirectoryInfo RequestDirectory { get; }

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
