namespace ParityBench.NET.Cli;

public static class RequestCommandParser
{
    private static readonly HashSet<string> OptionsWithValues = new HashSet<string>(StringComparer.Ordinal)
    {
        "--endpoint-a",
        "--endpoint-b",
        "--model",
        "--profile",
        "--concurrency",
        "--timeout",
        "--content-type",
        "--header",
        "--header-a",
        "--header-b",
        "--report-output",
        "--report-assets",
    };

    public static RequestCommandParseResult Parse(IReadOnlyList<string> args)
    {
        List<string> errors = new List<string>();
        if (args.Count == 0 || !string.Equals(args[0], "request", StringComparison.OrdinalIgnoreCase))
        {
            return Failure("Expected command: request <request-directory> --endpoint-a <url> --endpoint-b <url>.");
        }

        if (args.Count < 2 || args[1].StartsWith("--", StringComparison.Ordinal))
        {
            return Failure("Request directory is required.");
        }

        string requestDirectory = args[1];
        string? endpointA = null;
        string? endpointB = null;
        string modelName = "Auto";
        string? contractProfileId = null;
        int maxConcurrency = 4;
        TimeSpan timeout = TimeSpan.FromSeconds(30);
        string? contentTypeOverride = null;
        string? reportOutputDirectory = null;
        string? reportAssetsDirectory = null;
        List<string> commonHeaders = new List<string>();
        List<string> endpointAHeaders = new List<string>();
        List<string> endpointBHeaders = new List<string>();

        int index = 2;
        while (index < args.Count)
        {
            string option = args[index];
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
            }

            index += 2;
        }

        if (!TryCreateAbsoluteUri(endpointA, out Uri? endpointAUri))
        {
            errors.Add("--endpoint-a must be an absolute URL.");
        }

        if (!TryCreateAbsoluteUri(endpointB, out Uri? endpointBUri))
        {
            errors.Add("--endpoint-b must be an absolute URL.");
        }

        if (errors.Count > 0)
        {
            return new RequestCommandParseResult(null, errors);
        }

        return new RequestCommandParseResult(
            new RequestCommandOptions(
                requestDirectory,
                endpointAUri!,
                endpointBUri!,
                modelName,
                contractProfileId,
                maxConcurrency,
                timeout,
                contentTypeOverride,
                commonHeaders,
                endpointAHeaders,
                endpointBHeaders,
                reportOutputDirectory,
                reportAssetsDirectory),
            Array.Empty<string>());
    }

    public static string Usage =>
        "request <request-directory> --endpoint-a <url> --endpoint-b <url> [--model Auto] [--profile <profile-id>] [--concurrency <n>] [--timeout <seconds>] [--content-type <type>] [--header <Name: Value>] [--header-a <Name: Value>] [--header-b <Name: Value>] [--report-output <directory>] [--report-assets <directory>]";

    private static RequestCommandParseResult Failure(string error) =>
        new RequestCommandParseResult(null, new[] { error });

    private static bool TryCreateAbsoluteUri(string? value, out Uri? uri)
    {
        uri = null;
        return !string.IsNullOrWhiteSpace(value)
            && Uri.TryCreate(value, UriKind.Absolute, out uri);
    }
}
