using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunOptionsDto
{
    public string RequestBatch { get; init; } = string.Empty;

    public EndpointDefinitionDto EndpointA { get; init; } = new EndpointDefinitionDto();

    public EndpointDefinitionDto EndpointB { get; init; } = new EndpointDefinitionDto();

    public double TimeoutMilliseconds { get; init; }

    public int MaxConcurrency { get; init; }

    public string ResponseModelName { get; init; } = string.Empty;

    public string ModelName { get; init; } = "Auto";

    public ComparisonOptionsDto? Comparison { get; init; }

    public RequestExecutionOptionsDto? RequestExecution { get; init; }

    public ContractProfileSelectionDto? ContractProfile { get; init; }

    public PluginComparisonSelectionDto? PluginComparison { get; init; }

    public LargeRunOptionsDto? LargeRun { get; init; }

    public AlternateContractOptionsDto? AlternateContract { get; init; }

    public RetentionMode? RunRetentionModeOverride { get; init; }

    public string? ComparisonRulesSnapshotHash { get; init; }
}
