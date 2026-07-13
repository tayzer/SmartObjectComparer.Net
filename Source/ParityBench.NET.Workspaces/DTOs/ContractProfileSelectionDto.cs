using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class ContractProfileSelectionDto
{
    public string ProfileId { get; init; } = string.Empty;

    public string? ProfileVersion { get; init; }

    public Dictionary<string, string> Options { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
