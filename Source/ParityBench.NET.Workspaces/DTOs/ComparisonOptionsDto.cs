using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class ComparisonOptionsDto
{
    public bool IgnoreCollectionOrder { get; init; }

    public bool IgnoreStringCase { get; init; }

    public bool IgnoreTrailingWhitespaceAtEnd { get; init; }

    public bool TreatNullAndEmptyCollectionsAsEqual { get; init; }

    public bool IgnoreXmlNamespaces { get; init; } = true;

    public int MaxDifferences { get; init; } = 100;

    public bool IncludeAllDifferences { get; init; }

    public List<IgnoreRuleDefinitionDto> IgnoreRules { get; init; } = new List<IgnoreRuleDefinitionDto>();

    public List<SmartIgnoreRuleDefinitionDto> SmartIgnoreRules { get; init; } = new List<SmartIgnoreRuleDefinitionDto>();

    public List<MaskRuleDefinitionDto> MaskRules { get; init; } = new List<MaskRuleDefinitionDto>();
}
