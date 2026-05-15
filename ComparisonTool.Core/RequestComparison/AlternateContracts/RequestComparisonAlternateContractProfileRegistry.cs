using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Default in-memory registry for request-comparison alternate contract profiles.
/// </summary>
public sealed class RequestComparisonAlternateContractProfileRegistry : IRequestComparisonAlternateContractProfileRegistry
{
    private readonly ILogger<RequestComparisonAlternateContractProfileRegistry> logger;
    private readonly Dictionary<string, List<RequestComparisonAlternateContractProfile>> profilesByModel =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, RequestComparisonAlternateContractProfile> profilesById =
        new(StringComparer.Ordinal);

    public RequestComparisonAlternateContractProfileRegistry(
        ILogger<RequestComparisonAlternateContractProfileRegistry> logger,
        IEnumerable<RequestComparisonAlternateContractProfile> profiles)
    {
        this.logger = logger;

        foreach (var profile in profiles)
        {
            Register(profile);
        }
    }

    public void Register(RequestComparisonAlternateContractProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        profile.Validate();

        if (profilesById.ContainsKey(profile.ProfileId))
        {
            throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' is already registered.");
        }

        if (!profilesByModel.TryGetValue(profile.CanonicalModelName, out var modelProfiles))
        {
            modelProfiles = new List<RequestComparisonAlternateContractProfile>();
            profilesByModel[profile.CanonicalModelName] = modelProfiles;
        }

        modelProfiles.Add(profile);
        profilesById[profile.ProfileId] = profile;

        logger.LogInformation(
            "Registered alternate contract profile {ProfileId} for model {ModelName}",
            profile.ProfileId,
            profile.CanonicalModelName);
    }

    public RequestComparisonAlternateContractProfile Resolve(string canonicalModelName, string? profileId = null)
    {
        if (TryResolve(canonicalModelName, profileId, out var profile, out var errorMessage))
        {
            return profile!;
        }

        throw new InvalidOperationException(errorMessage ?? "Alternate contract profile could not be resolved.");
    }

    public bool TryResolve(
        string canonicalModelName,
        string? profileId,
        out RequestComparisonAlternateContractProfile? profile,
        out string? errorMessage)
    {
        profile = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(canonicalModelName))
        {
            errorMessage = "A canonical model name is required to resolve an alternate contract profile.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(profileId))
        {
            if (!profilesById.TryGetValue(profileId, out profile))
            {
                errorMessage = $"Alternate contract profile '{profileId}' is not registered.";
                return false;
            }

            if (!string.Equals(profile.CanonicalModelName, canonicalModelName, StringComparison.Ordinal))
            {
                errorMessage = $"Alternate contract profile '{profileId}' targets canonical model '{profile.CanonicalModelName}', not '{canonicalModelName}'.";
                profile = null;
                return false;
            }

            return true;
        }

        if (!profilesByModel.TryGetValue(canonicalModelName, out var modelProfiles) || modelProfiles.Count == 0)
        {
            errorMessage = $"No alternate contract profiles are registered for canonical model '{canonicalModelName}'.";
            return false;
        }

        if (modelProfiles.Count > 1)
        {
            errorMessage = $"Multiple alternate contract profiles are registered for canonical model '{canonicalModelName}'. Specify AlternateContractProfileId explicitly.";
            return false;
        }

        profile = modelProfiles[0];
        return true;
    }

    public IReadOnlyList<string> GetProfileIds(string canonicalModelName)
    {
        if (!profilesByModel.TryGetValue(canonicalModelName, out var modelProfiles))
        {
            return Array.Empty<string>();
        }

        return modelProfiles
            .Select(profile => profile.ProfileId)
            .OrderBy(profileId => profileId, StringComparer.Ordinal)
            .ToArray();
    }
}
