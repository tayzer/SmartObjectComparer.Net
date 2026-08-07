using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Infrastructure;

public sealed class ContractProfileRegistry : IContractProfileRegistry
{
    private readonly Dictionary<string, List<IContractProfile>> profilesByModel =
        new Dictionary<string, List<IContractProfile>>(StringComparer.Ordinal);
    private readonly Dictionary<string, IContractProfile> profilesById =
        new Dictionary<string, IContractProfile>(StringComparer.Ordinal);
    private readonly object gate = new object();

    public void Register(IContractProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);

        lock (gate)
        {
            if (profilesById.ContainsKey(profile.ProfileId))
            {
                throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' is already registered.");
            }

            if (!profilesByModel.TryGetValue(profile.ResponseModelName, out List<IContractProfile>? modelProfiles))
            {
                modelProfiles = new List<IContractProfile>();
                profilesByModel[profile.ResponseModelName] = modelProfiles;
            }

            modelProfiles.Add(profile);
            profilesById[profile.ProfileId] = profile;
        }
    }

    public IContractProfile Resolve(string responseModelName, ContractProfileSelection? selection = null)
    {
        if (TryResolve(responseModelName, selection, out IContractProfile? profile, out string? errorMessage))
        {
            return profile!;
        }

        throw new InvalidOperationException(errorMessage ?? "Contract profile could not be resolved.");
    }

    public bool TryResolve(
        string responseModelName,
        ContractProfileSelection? selection,
        out IContractProfile? profile,
        out string? errorMessage)
    {
        string modelName = string.IsNullOrWhiteSpace(responseModelName) ? "Auto" : responseModelName.Trim();
        profile = null;
        errorMessage = null;

        if (selection is null || string.Equals(selection.ProfileId, ContractProfileSelection.SameContractProfileId, StringComparison.Ordinal))
        {
            profile = new SameContractProfile(modelName);
            return true;
        }

        lock (gate)
        {
            if (!profilesById.TryGetValue(selection.ProfileId, out profile))
            {
                errorMessage = $"Contract profile '{selection.ProfileId}' is not registered.";
                return false;
            }

            if (!string.Equals(profile.ResponseModelName, modelName, StringComparison.Ordinal))
            {
                errorMessage = $"Contract profile '{selection.ProfileId}' targets response model '{profile.ResponseModelName}', not '{modelName}'.";
                profile = null;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(selection.ProfileVersion)
                && !string.Equals(selection.ProfileVersion, profile.ProfileVersion, StringComparison.Ordinal))
            {
                errorMessage = $"Contract profile '{selection.ProfileId}' is version '{profile.ProfileVersion}', not requested version '{selection.ProfileVersion}'.";
                profile = null;
                return false;
            }

            return true;
        }
    }

    public IReadOnlyList<string> GetProfileIds(string responseModelName)
    {
        string modelName = string.IsNullOrWhiteSpace(responseModelName) ? "Auto" : responseModelName.Trim();
        lock (gate)
        {
            List<string> profileIds = profilesByModel.TryGetValue(modelName, out List<IContractProfile>? modelProfiles)
                ? modelProfiles.Select(profile => profile.ProfileId).ToList()
                : new List<string>();

            profileIds.Add(ContractProfileSelection.SameContractProfileId);
            return profileIds.OrderBy(profileId => profileId, StringComparer.Ordinal).ToList();
        }
    }

    private static void ValidateProfile(IContractProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            throw new InvalidOperationException("Contract profile id is required.");
        }

        if (string.Equals(profile.ProfileId, ContractProfileSelection.SameContractProfileId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"'{ContractProfileSelection.SameContractProfileId}' is reserved for the built-in profile.");
        }

        if (string.IsNullOrWhiteSpace(profile.ResponseModelName))
        {
            throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' must specify a response model name.");
        }

        if (profile.EndpointA.SupportedSourceRequestFormats.Count == 0)
        {
            throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' must support at least one endpoint A request format.");
        }

        if (string.IsNullOrWhiteSpace(profile.CanonicalResponseContentType))
        {
            throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' must specify a canonical comparison response content type.");
        }

        foreach (IgnoreRuleDefinition rule in profile.DefaultIgnoreRules)
        {
            if (string.IsNullOrWhiteSpace(rule.PropertyPath))
            {
                throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' contains a default ignore rule with an empty property path.");
            }
        }

        foreach (KeyValuePair<string, string> mapping in profile.CanonicalToEndpointResponseMaskPathMap)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key) || string.IsNullOrWhiteSpace(mapping.Value))
            {
                throw new InvalidOperationException($"Contract profile '{profile.ProfileId}' contains an invalid response mask path mapping.");
            }
        }
    }
}

