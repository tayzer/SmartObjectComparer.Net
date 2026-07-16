namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Resolves request-comparison alternate contract profiles registered through DI.
/// </summary>
public interface IRequestComparisonAlternateContractProfileRegistry
{
    /// <summary>
    /// Registers a profile with the registry.
    /// </summary>
    void Register(RequestComparisonAlternateContractProfile profile);

    /// <summary>
    /// Resolves a profile for the selected canonical model and optional explicit profile identifier.
    /// </summary>
    RequestComparisonAlternateContractProfile Resolve(string canonicalModelName, string? profileId = null);

    /// <summary>
    /// Attempts to resolve a profile.
    /// </summary>
    bool TryResolve(string canonicalModelName, string? profileId, out RequestComparisonAlternateContractProfile? profile, out string? errorMessage);

    /// <summary>
    /// Returns the profile identifiers registered for the canonical model.
    /// </summary>
    IReadOnlyList<string> GetProfileIds(string canonicalModelName);
}
