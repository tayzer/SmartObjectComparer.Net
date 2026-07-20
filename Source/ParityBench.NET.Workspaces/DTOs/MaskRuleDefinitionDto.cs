using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class MaskRuleDefinitionDto
{
    public string PropertyPath { get; init; } = string.Empty;

    public int PreserveLastCharacters { get; init; }

    public string MaskCharacter { get; init; } = "*";
}
