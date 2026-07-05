using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
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
        string modelName = "Auto",
        ComparisonOptions? comparisonOptions = null,
        RequestExecutionOptions? requestExecutionOptions = null,
        AlternateContractOptions? alternateContractOptions = null)
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
        ModelName = string.IsNullOrWhiteSpace(modelName) ? "Auto" : modelName;
        Comparison = comparisonOptions ?? new ComparisonOptions();
        RequestExecution = requestExecutionOptions ?? new RequestExecutionOptions();
        AlternateContract = alternateContractOptions;
    }

    public RequestBatchReference RequestBatch { get; }

    public EndpointDefinition EndpointA { get; }

    public EndpointDefinition EndpointB { get; }

    public TimeSpan Timeout { get; }

    public int MaxConcurrency { get; }

    public string ModelName { get; }

    public ComparisonOptions Comparison { get; }

    public RequestExecutionOptions RequestExecution { get; }

    public AlternateContractOptions? AlternateContract { get; }
}
