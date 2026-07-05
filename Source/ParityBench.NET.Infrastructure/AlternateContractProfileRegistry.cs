using ParityBench.NET.Application.AlternateContracts;

namespace ParityBench.NET.Infrastructure;

public sealed class AlternateContractProfileRegistry : IAlternateContractProfileRegistry
{
    private readonly Dictionary<string, List<IAlternateContractProfile>> profilesByModel =
        new Dictionary<string, List<IAlternateContractProfile>>(StringComparer.Ordinal);
    private readonly Dictionary<string, IAlternateContractProfile> profilesById =
        new Dictionary<string, IAlternateContractProfile>(StringComparer.Ordinal);
    private readonly object gate = new object();

    public void Register(IAlternateContractProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateProfile(profile);

        lock (gate)
        {
            if (profilesById.ContainsKey(profile.ProfileId))
            {
                throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' is already registered.");
            }

            if (!profilesByModel.TryGetValue(profile.CanonicalModelName, out List<IAlternateContractProfile>? modelProfiles))
            {
                modelProfiles = new List<IAlternateContractProfile>();
                profilesByModel[profile.CanonicalModelName] = modelProfiles;
            }

            modelProfiles.Add(profile);
            profilesById[profile.ProfileId] = profile;
        }
    }

    public IAlternateContractProfile Resolve(string canonicalModelName, string? profileId = null)
    {
        if (TryResolve(canonicalModelName, profileId, out IAlternateContractProfile? profile, out string? errorMessage))
        {
            return profile!;
        }

        throw new InvalidOperationException(errorMessage ?? "Alternate contract profile could not be resolved.");
    }

    public bool TryResolve(
        string canonicalModelName,
        string? profileId,
        out IAlternateContractProfile? profile,
        out string? errorMessage)
    {
        profile = null;
        errorMessage = null;

        if (string.IsNullOrWhiteSpace(canonicalModelName))
        {
            errorMessage = "A canonical model name is required to resolve an alternate contract profile.";
            return false;
        }

        lock (gate)
        {
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

            if (!profilesByModel.TryGetValue(canonicalModelName, out List<IAlternateContractProfile>? modelProfiles) || modelProfiles.Count == 0)
            {
                errorMessage = $"No alternate contract profiles are registered for canonical model '{canonicalModelName}'.";
                return false;
            }

            if (modelProfiles.Count > 1)
            {
                errorMessage = $"Multiple alternate contract profiles are registered for canonical model '{canonicalModelName}'. Specify an alternate contract profile id explicitly.";
                return false;
            }

            profile = modelProfiles[0];
            return true;
        }
    }

    public IReadOnlyList<string> GetProfileIds(string canonicalModelName)
    {
        lock (gate)
        {
            return profilesByModel.TryGetValue(canonicalModelName, out List<IAlternateContractProfile>? modelProfiles)
                ? modelProfiles.Select(profile => profile.ProfileId).OrderBy(profileId => profileId, StringComparer.Ordinal).ToList()
                : Array.Empty<string>();
        }
    }

    private static void ValidateProfile(IAlternateContractProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProfileId))
        {
            throw new InvalidOperationException("Alternate contract profile id is required.");
        }

        if (string.IsNullOrWhiteSpace(profile.CanonicalModelName))
        {
            throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' must specify a canonical model name.");
        }

        if (profile.SupportedSourceRequestFormats.Count == 0)
        {
            throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' must support at least one source request format.");
        }

        if (string.IsNullOrWhiteSpace(profile.AlternateRequestContentType))
        {
            throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' must specify an endpoint B request content type.");
        }

        if (string.IsNullOrWhiteSpace(profile.CanonicalResponseContentType))
        {
            throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' must specify a canonical comparison response content type.");
        }

        foreach (IgnoreRuleValidationCandidate rule in profile.DefaultIgnoreRules.Select(rule => new IgnoreRuleValidationCandidate(rule.PropertyPath)))
        {
            if (string.IsNullOrWhiteSpace(rule.PropertyPath))
            {
                throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' contains a default ignore rule with an empty property path.");
            }
        }

        foreach (KeyValuePair<string, string> mapping in profile.CanonicalToAlternateResponseMaskPathMap)
        {
            if (string.IsNullOrWhiteSpace(mapping.Key) || string.IsNullOrWhiteSpace(mapping.Value))
            {
                throw new InvalidOperationException($"Alternate contract profile '{profile.ProfileId}' contains an invalid response mask path mapping.");
            }
        }
    }

    private sealed record IgnoreRuleValidationCandidate(string PropertyPath);
}
