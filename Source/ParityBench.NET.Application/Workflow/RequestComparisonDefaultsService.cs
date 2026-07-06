using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.Workflow;

public sealed class RequestComparisonDefaultsService : IRequestComparisonDefaultsUseCases
{
    private readonly IResponseModelRegistry responseModelRegistry;
    private readonly IContractProfileRegistry contractProfileRegistry;
    private readonly IRequestComparisonEndpointRegistry endpointRegistry;
    private readonly IRequestComparisonPresetRegistry presetRegistry;

    public RequestComparisonDefaultsService(
        IResponseModelRegistry responseModelRegistry,
        IContractProfileRegistry contractProfileRegistry,
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry)
    {
        this.responseModelRegistry = responseModelRegistry ?? throw new ArgumentNullException(nameof(responseModelRegistry));
        this.contractProfileRegistry = contractProfileRegistry ?? throw new ArgumentNullException(nameof(contractProfileRegistry));
        this.endpointRegistry = endpointRegistry ?? throw new ArgumentNullException(nameof(endpointRegistry));
        this.presetRegistry = presetRegistry ?? throw new ArgumentNullException(nameof(presetRegistry));
    }

    public Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<ResponseModelOption> responseModels = responseModelRegistry
            .ListModelNames()
            .OrderBy(modelName => modelName, StringComparer.OrdinalIgnoreCase)
            .Select(modelName => new ResponseModelOption(modelName))
            .ToArray();

        IReadOnlyList<ContractProfileOption> contractProfiles = responseModels
            .SelectMany(model => LoadProfileOptions(model.ModelName))
            .OrderBy(profile => profile.ResponseModelName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(profile => profile.ProfileId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RequestComparisonDefaults defaults = new RequestComparisonDefaults(
            responseModels,
            contractProfiles,
            endpointRegistry.ListEndpoints(),
            presetRegistry.ListPresets());

        return Task.FromResult(defaults);
    }

    private IEnumerable<ContractProfileOption> LoadProfileOptions(string modelName)
    {
        foreach (string profileId in contractProfileRegistry.GetProfileIds(modelName))
        {
            IContractProfile profile = contractProfileRegistry.Resolve(modelName, new ContractProfileSelection(profileId));
            yield return new ContractProfileOption(
                profile.ResponseModelName,
                profile.ProfileId,
                profile.ProfileVersion,
                profile.EndpointA.SuggestedEndpointId,
                profile.EndpointB.SuggestedEndpointId,
                profile.DefaultIgnoreRules.ToArray());
        }
    }
}
