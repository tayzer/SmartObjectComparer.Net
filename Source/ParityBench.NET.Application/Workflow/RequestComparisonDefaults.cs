using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.Workflow;

public sealed record RequestComparisonDefaults(
    IReadOnlyList<ResponseModelOption> ResponseModels,
    IReadOnlyList<ContractProfileOption> ContractProfiles,
    IReadOnlyList<EndpointOption> Endpoints,
    IReadOnlyList<RequestComparisonPresetOption> Presets);

public sealed record ResponseModelOption(string ModelName);

public sealed record ContractProfileOption
{
    public ContractProfileOption(
        string responseModelName,
        string profileId,
        string? profileVersion,
        string? endpointASuggestedEndpointId,
        string? endpointBSuggestedEndpointId,
        ComparisonRuleDefaults defaultComparisonRules)
    {
        ResponseModelName = responseModelName;
        ProfileId = profileId;
        ProfileVersion = profileVersion;
        EndpointASuggestedEndpointId = endpointASuggestedEndpointId;
        EndpointBSuggestedEndpointId = endpointBSuggestedEndpointId;
        DefaultComparisonRules = defaultComparisonRules;
    }

    public ContractProfileOption(
        string responseModelName,
        string profileId,
        string? profileVersion,
        string? endpointASuggestedEndpointId,
        string? endpointBSuggestedEndpointId,
        IReadOnlyList<IgnoreRuleDefinition> defaultIgnoreRules)
        : this(
            responseModelName,
            profileId,
            profileVersion,
            endpointASuggestedEndpointId,
            endpointBSuggestedEndpointId,
            new ComparisonRuleDefaults(ignoreRules: defaultIgnoreRules))
    {
    }

    public string ResponseModelName { get; }

    public string ProfileId { get; }

    public string? ProfileVersion { get; }

    public string? EndpointASuggestedEndpointId { get; }

    public string? EndpointBSuggestedEndpointId { get; }

    public ComparisonRuleDefaults DefaultComparisonRules { get; }

    public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules => DefaultComparisonRules.IgnoreRules;
}

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
