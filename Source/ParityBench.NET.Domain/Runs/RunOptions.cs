using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Runs;

public sealed record RunOptions
{
    public RunOptions(
        RequestBatchReference requestBatch,
        EndpointDefinition endpointA,
        EndpointDefinition endpointB,
        TimeSpan timeout,
        int maxConcurrency,
        string responseModelName = "Auto",
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        ContractProfileSelection? contractProfileSelection = null)
    {
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

        RequestBatch = requestBatch;
        EndpointA = endpointA;
        EndpointB = endpointB;
        Timeout = timeout;
        MaxConcurrency = maxConcurrency;
        ResponseModelName = string.IsNullOrWhiteSpace(responseModelName) ? "Auto" : responseModelName.Trim();
        Comparison = comparisonOptions ?? new ComparisonOptions();
        RequestExecution = requestExecutionOptions ?? new RequestExecutionOptions();
        ContractProfile = contractProfileSelection;
    }

    public RequestBatchReference RequestBatch { get; }

    public EndpointDefinition EndpointA { get; }

    public EndpointDefinition EndpointB { get; }

    public TimeSpan Timeout { get; }

    public int MaxConcurrency { get; }

    public string ResponseModelName { get; }

    public string ModelName => ResponseModelName;

    public ComparisonOptions Comparison { get; }

    public RequestExecutionOptions RequestExecution { get; }

    public ContractProfileSelection? ContractProfile { get; }
}
