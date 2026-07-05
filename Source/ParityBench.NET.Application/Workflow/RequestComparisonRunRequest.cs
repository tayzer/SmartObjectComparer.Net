using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

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
        AlternateContractOptions? alternateContractOptions = null,
        IReadOnlyDictionary<string, string>? commonHeaders = null,
        IReadOnlyDictionary<string, string>? endpointAHeaders = null,
        IReadOnlyDictionary<string, string>? endpointBHeaders = null,
        string? endpointALabel = null,
        string? endpointBLabel = null)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory))
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

        SourceDirectory = sourceDirectory;
        EndpointA = endpointA;
        EndpointB = endpointB;
        Timeout = timeout;
        MaxConcurrency = maxConcurrency;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? "Auto" : modelName.Trim();
        ComparisonOptions = comparisonOptions ?? new ComparisonOptions();
        RequestExecutionOptions = requestExecutionOptions ?? new RequestExecutionOptions();
        AlternateContractOptions = alternateContractOptions;
        CommonHeaders = CopyHeaders(commonHeaders);
        EndpointAHeaders = CopyHeaders(endpointAHeaders);
        EndpointBHeaders = CopyHeaders(endpointBHeaders);
        EndpointALabel = string.IsNullOrWhiteSpace(endpointALabel) ? null : endpointALabel.Trim();
        EndpointBLabel = string.IsNullOrWhiteSpace(endpointBLabel) ? null : endpointBLabel.Trim();
    }

    public string SourceDirectory { get; }

    public Uri EndpointA { get; }

    public Uri EndpointB { get; }

    public TimeSpan Timeout { get; }

    public int MaxConcurrency { get; }

    public string ModelName { get; }

    public ComparisonOptions ComparisonOptions { get; }

    public RequestExecutionOptions RequestExecutionOptions { get; }

    public AlternateContractOptions? AlternateContractOptions { get; }

    public IReadOnlyDictionary<string, string> CommonHeaders { get; }

    public IReadOnlyDictionary<string, string> EndpointAHeaders { get; }

    public IReadOnlyDictionary<string, string> EndpointBHeaders { get; }

    public string? EndpointALabel { get; }

    public string? EndpointBLabel { get; }

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
}
