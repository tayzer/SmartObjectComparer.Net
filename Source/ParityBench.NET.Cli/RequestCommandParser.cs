using Microsoft.Extensions.Logging;

namespace ParityBench.NET.Cli;

public static class RequestCommandParser
{
    private static readonly HashSet<string> OptionsWithValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "--endpoint-a",
        "--endpoint-b",
        "--model",
        "--profile",
        "--preset",
        "--concurrency",
        "--timeout",
        "--content-type",
        "--header",
        "--header-a",
        "--header-b",
        "--report-output",
        "--report-assets",
        "--log-level",
        "--slow-path-threshold-ms",
    };

    private static readonly HashSet<string> FlagOptions = new HashSet<string>(StringComparer.Ordinal)
    {
        "--log-durations",
        "--log-exceptions",
        "--persist-diagnostics",
    };

    public static RequestCommandParseResult Parse(IReadOnlyList<string> args)
    {
        List<string> errors = new List<string>();
        if (args.Count == 0 || !string.Equals(args[0], "request", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Expected command: request [<request-directory>] --endpoint-a <url> --endpoint-b <url> | --preset <preset-id>.");
        }

        int index = 1;
        string? requestDirectory = null;
        if (index < args.Count && !args[index].StartsWith("--", StringComparison.Ordinal))
        {
            requestDirectory = args[index];
            index++;
        }

        string? endpointA = null;
        string? endpointB = null;
        string? modelName = null;
        string? contractProfileId = null;
        string? presetId = null;
        int maxConcurrency = 4;
        TimeSpan timeout = TimeSpan.FromSeconds(30);
        string? contentTypeOverride = null;
        string? reportOutputDirectory = null;
        string? reportAssetsDirectory = null;
        LogLevel? logLevel = null;
        bool logDurations = false;
        bool logExceptions = false;
        bool persistDiagnostics = false;
        int? slowPathThresholdMs = null;
        List<string> commonHeaders = new List<string>();
        List<string> endpointAHeaders = new List<string>();
        List<string> endpointBHeaders = new List<string>();

        while (index < args.Count)
        {
            string option = args[index];
            if (FlagOptions.Contains(option))
            {
                switch (option)
                {
                    case "--log-durations":
                        logDurations = true;
                        break;
                    case "--log-exceptions":
                        logExceptions = true;
                        break;
                    case "--persist-diagnostics":
                        persistDiagnostics = true;
                        break;
                }

                index++;
                continue;
            }

            if (!OptionsWithValues.Contains(option))
            {
                errors.Add($"Unknown option '{option}'.");
                index++;
                continue;
            }

            if (index + 1 >= args.Count || args[index + 1].StartsWith("--", StringComparison.Ordinal))
            {
                errors.Add($"Option '{option}' requires a value.");
                index++;
                continue;
            }

            string value = args[index + 1];
            switch (option)
            {
                case "--endpoint-a":
                    endpointA = value;
                    break;
                case "--endpoint-b":
                    endpointB = value;
                    break;
                case "--model":
                    modelName = string.IsNullOrWhiteSpace(value) ? "Auto" : value;
                    break;
                case "--profile":
                    contractProfileId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    break;
                case "--preset":
                    presetId = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    break;
                case "--concurrency":
                    if (!int.TryParse(value, out maxConcurrency) || maxConcurrency <= 0)
                    {
                        errors.Add("Concurrency must be a positive whole number.");
                    }
                    break;
                case "--timeout":
                    if (!int.TryParse(value, out int timeoutSeconds) || timeoutSeconds <= 0)
                    {
                        errors.Add("Timeout must be a positive whole number of seconds.");
                    }
                    else
                    {
                        timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    }
                    break;
                case "--content-type":
                    contentTypeOverride = value;
                    break;
                case "--header":
                    commonHeaders.Add(value);
                    break;
                case "--header-a":
                    endpointAHeaders.Add(value);
                    break;
                case "--header-b":
                    endpointBHeaders.Add(value);
                    break;
                case "--report-output":
                    reportOutputDirectory = value;
                    break;
                case "--report-assets":
                    reportAssetsDirectory = value;
                    break;
                case "--log-level":
                    if (Enum.TryParse(value, ignoreCase: true, out LogLevel parsedLogLevel))
                    {
                        logLevel = parsedLogLevel;
                    }
                    else
                    {
                        errors.Add("--log-level must be one of Trace, Debug, Information, Warning, Error, Critical, or None.");
                    }
                    break;
                case "--slow-path-threshold-ms":
                    if (!int.TryParse(value, out int threshold) || threshold < 0)
                    {
                        errors.Add("--slow-path-threshold-ms must be zero or a positive whole number.");
                    }
                    else
                    {
                        slowPathThresholdMs = threshold;
                    }
                    break;
            }

            index += 2;
        }

        bool hasPreset = !string.IsNullOrWhiteSpace(presetId);
        Uri? endpointAUri = null;
        Uri? endpointBUri = null;

        if (endpointA is not null || !hasPreset)
        {
            if (!TryCreateAbsoluteUri(endpointA, out endpointAUri))
            {
                errors.Add("--endpoint-a must be an absolute URL.");
            }
        }

        if (endpointB is not null || !hasPreset)
        {
            if (!TryCreateAbsoluteUri(endpointB, out endpointBUri))
            {
                errors.Add("--endpoint-b must be an absolute URL.");
            }
        }

        if (requestDirectory is null && !hasPreset)
        {
            errors.Add("Request directory is required unless --preset is specified.");
        }

        if (errors.Count > 0)
        {
            return new RequestCommandParseResult(null, errors);
        }

        return new RequestCommandParseResult(
            new RequestCommandOptions(
                requestDirectory,
                endpointAUri,
                endpointBUri,
                modelName,
                contractProfileId,
                maxConcurrency,
                timeout,
                contentTypeOverride,
                commonHeaders,
                endpointAHeaders,
                endpointBHeaders,
                reportOutputDirectory,
                reportAssetsDirectory,
                new ObservabilityCliOptions(logLevel, logDurations, logExceptions, persistDiagnostics, slowPathThresholdMs),
                presetId),
            Array.Empty<string>());
    }

    public static string Usage =>
        "request [<request-directory>] --endpoint-a <url> --endpoint-b <url> | --preset <preset-id> [--model Auto] [--profile <profile-id>] [--concurrency <n>] [--timeout <seconds>] [--content-type <type>] [--header <Name: Value>] [--header-a <Name: Value>] [--header-b <Name: Value>] [--report-output <directory>] [--report-assets <directory>] [--log-level <level>] [--log-durations] [--log-exceptions] [--persist-diagnostics] [--slow-path-threshold-ms <n>]";

    private static RequestCommandParseResult Failure(string error) =>
        new RequestCommandParseResult(null, new[] { error });

    private static bool TryCreateAbsoluteUri(string? value, out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out uri);
    }
}
