using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Application.Workflow;

public sealed record RequestComparisonRunRequest
{
    public RequestComparisonRunRequest(
        string sourceDirectory,
        Uri endpointA,
        Uri endpointB,
        TimeSpan timeout,
        int maxConcurrency,
        string modelName = "Auto",
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        ContractProfileSelection? contractProfileSelection = null,
        IReadOnlyDictionary<string, string>? commonHeaders = null,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        IReadOnlyDictionary<string, string>? endpointBHeaders = null,
        string? endpointALabel = null,
        string? endpointBLabel = null,
        IReadOnlyList<string>? sourceFiles = null,
        RetentionMode? runRetentionModeOverride = null,
        PluginComparisonSelection? pluginComparison = null,
        BaselineRunSelection? baseline = null)
    {
        // A replay run takes its requests from the baseline package, so it is the one
        // case where a host has no source directory to offer.
        if (string.IsNullOrWhiteSpace(sourceDirectory) && baseline?.Mode != BaselineRunMode.BaselineVsLive)
        {
            throw new ArgumentException("Source directory must not be empty.", nameof(sourceDirectory));
        }

        ArgumentNullException.ThrowIfNull(endpointA);
        ArgumentNullException.ThrowIfNull(endpointB);

        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Timeout must be greater than zero.");
        }

        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Maximum concurrency must be greater than zero.");
        }

        SourceDirectory = sourceDirectory ?? string.Empty;
        EndpointA = endpointA;
        EndpointB = endpointB;
        Timeout = timeout;
        MaxConcurrency = maxConcurrency;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? "Auto" : modelName.Trim();
        ComparisonOptions = comparisonOptions ?? new ComparisonOptions();
        RequestExecutionOptions = requestExecutionOptions ?? new RequestExecutionOptions();
        ContractProfileSelection = contractProfileSelection;
        CommonHeaders = CopyHeaders(commonHeaders);
        EndpointAHeaders = CopyHeaders(endpointAHeaders);
        EndpointBHeaders = CopyHeaders(endpointBHeaders);
        EndpointALabel = string.IsNullOrWhiteSpace(endpointALabel) ? null : endpointALabel.Trim();
        EndpointBLabel = string.IsNullOrWhiteSpace(endpointBLabel) ? null : endpointBLabel.Trim();
        SourceFiles = CopySourceFiles(sourceFiles);
        RunRetentionModeOverride = runRetentionModeOverride;
        PluginComparison = pluginComparison;
        Baseline = baseline;
    }

    public string SourceDirectory { get; }

    public IReadOnlyList<string> SourceFiles { get; }

    public Uri EndpointA { get; }

    public Uri EndpointB { get; }

    public TimeSpan Timeout { get; }

    public int MaxConcurrency { get; }

    public string ModelName { get; }

    public ComparisonOptions ComparisonOptions { get; }

    public RequestExecutionOptions RequestExecutionOptions { get; }

    public ContractProfileSelection? ContractProfileSelection { get; }

    public IReadOnlyDictionary<string, string> CommonHeaders { get; }

    public IReadOnlyDictionary<string, string> EndpointAHeaders { get; }

    public IReadOnlyDictionary<string, string> EndpointBHeaders { get; }

    public string? EndpointALabel { get; }

    public string? EndpointBLabel { get; }

    public RetentionMode? RunRetentionModeOverride { get; }

    /// <summary>
    /// Gets the plugin comparison this run selected, if any. Its step configuration
    /// must already have secret references resolved to values.
    /// </summary>
    public PluginComparisonSelection? PluginComparison { get; }

    /// <summary>
    /// Gets what this run does with baselines: capture one from endpoint A, replay
    /// one as the expected side, or neither.
    /// </summary>
    public BaselineRunSelection? Baseline { get; }

    private static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        Dictionary<string, string> copied = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (!string.IsNullOrWhiteSpace(header.Key))
                {
                    copied[header.Key] = header.Value;
                }
            }
        }

        return copied;
    }

    private static IReadOnlyList<string> CopySourceFiles(IReadOnlyList<string>? sourceFiles)
    {
        if (sourceFiles is null || sourceFiles.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> copied = new List<string>(sourceFiles.Count);
        foreach (string sourceFile in sourceFiles)
        {
            if (string.IsNullOrWhiteSpace(sourceFile))
            {
                throw new ArgumentException("Source file paths must not be empty.", nameof(sourceFiles));
            }

            copied.Add(sourceFile);
        }

        return copied;
    }
}

