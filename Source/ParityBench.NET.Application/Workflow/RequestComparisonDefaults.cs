using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.Workflow;

public sealed record RequestComparisonDefaults
{
    public RequestComparisonDefaults(
        IReadOnlyList<ResponseModelOption> responseModels,
        IReadOnlyList<ContractProfileOption> contractProfiles,
        IReadOnlyList<EndpointOption> endpoints,
        IReadOnlyList<RequestComparisonPresetOption> presets)
        : this(responseModels, contractProfiles, endpoints, presets, new RequestComparisonRunDefaults())
    {
    }

    public RequestComparisonDefaults(
        IReadOnlyList<ResponseModelOption> responseModels,
        IReadOnlyList<ContractProfileOption> contractProfiles,
        IReadOnlyList<EndpointOption> endpoints,
        IReadOnlyList<RequestComparisonPresetOption> presets,
        RequestComparisonRunDefaults runDefaults)
    {
        ResponseModels = responseModels;
        ContractProfiles = contractProfiles;
        Endpoints = endpoints;
        Presets = presets;
        RunDefaults = runDefaults;
    }

    public IReadOnlyList<ResponseModelOption> ResponseModels { get; }

    public IReadOnlyList<ContractProfileOption> ContractProfiles { get; }

    public IReadOnlyList<EndpointOption> Endpoints { get; }

    public IReadOnlyList<RequestComparisonPresetOption> Presets { get; }

    public RequestComparisonRunDefaults RunDefaults { get; }
}

public sealed record RequestComparisonRunDefaults
{
    public RequestComparisonRunDefaults()
        : this(32, 30)
    {
    }

    public RequestComparisonRunDefaults(int maxConcurrency = 32, int timeoutSeconds = 30)
    {
        if (maxConcurrency <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConcurrency), "Default maximum concurrency must be greater than zero.");
        }

        if (timeoutSeconds <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(timeoutSeconds), "Default timeout seconds must be greater than zero.");
        }

        MaxConcurrency = maxConcurrency;
        TimeoutSeconds = timeoutSeconds;
    }

    public int MaxConcurrency { get; init; }

    public int TimeoutSeconds { get; init; }
}

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
    Uri Url,
    IReadOnlyDictionary<string, string>? DefaultHeaders = null);

public sealed record RequestComparisonPresetOption(
    string PresetId,
    string Label,
    string RequestDirectory,
    Uri EndpointA,
    Uri EndpointB,
    string ModelName,
    string? ContractProfileId,
    ComparisonOptions ComparisonOptions,
    RequestExecutionOptions RequestExecutionOptions,
    IReadOnlyDictionary<string, string>? EndpointAHeaders = null,
    IReadOnlyDictionary<string, string>? EndpointBHeaders = null);
