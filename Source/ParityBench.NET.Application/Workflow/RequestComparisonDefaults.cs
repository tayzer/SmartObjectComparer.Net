using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.Workflow;

public sealed record RequestComparisonDefaults(
    IReadOnlyList<ResponseModelOption> ResponseModels,
    IReadOnlyList<ContractProfileOption> ContractProfiles,
    IReadOnlyList<EndpointOption> Endpoints,
    IReadOnlyList<RequestComparisonPresetOption> Presets);

public sealed record ResponseModelOption(string ModelName);

public sealed record ContractProfileOption(
    string ResponseModelName,
    string ProfileId,
    string? ProfileVersion,
    string? EndpointASuggestedEndpointId,
    string? EndpointBSuggestedEndpointId,
    IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules);

public sealed record EndpointOption(
    string EndpointId,
    string Label,
    Uri Url);

public sealed record RequestComparisonPresetOption(
    string PresetId,
    string Label,
    string RequestDirectory,
    Uri EndpointA,
    Uri EndpointB,
    string ModelName,
    string? ContractProfileId,
    ComparisonOptions ComparisonOptions,
    RequestExecutionOptions RequestExecutionOptions);
