using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class SmartIgnoreRuleDefinitionDto
{
    public SmartIgnoreRuleKind Kind { get; init; }

    public string Value { get; init; } = string.Empty;

    public bool IsEnabled { get; init; } = true;

    public string? Description { get; init; }
}
