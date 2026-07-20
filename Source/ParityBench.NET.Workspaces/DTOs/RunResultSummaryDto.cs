using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunResultSummaryDto
{
    public int TotalPairs { get; init; }

    public int EqualPairs { get; init; }

    public int DifferentPairs { get; init; }

    public int ErrorPairs { get; init; }

    public int StatusCodeMismatchPairs { get; init; }

    public int BothNonSuccessPairs { get; init; }

    public RunDetailReferenceDto? DetailIndexReference { get; init; }

    public RunExecutionMetricsDto? ExecutionMetrics { get; init; }
}
