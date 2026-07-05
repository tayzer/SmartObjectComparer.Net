namespace ParityBench.NET.Application.AlternateContracts;

/// <summary>
/// Resolves alternate-contract profiles registered by a host or infrastructure module.
/// </summary>
public interface IAlternateContractProfileRegistry
{
    /// <summary>
    /// Registers a profile with a unique profile id.
    /// </summary>
    void Register(IAlternateContractProfile profile);

    /// <summary>
    /// Resolves a profile for the canonical model and optional explicit profile id.
    /// </summary>
    IAlternateContractProfile Resolve(string canonicalModelName, string? profileId = null);

    /// <summary>
    /// Attempts to resolve a profile without throwing.
    /// </summary>
    bool TryResolve(
        string canonicalModelName,
        string? profileId,
        out IAlternateContractProfile? profile,
        out string? errorMessage);

    /// <summary>
    /// Lists profile ids registered for a canonical model.
    /// </summary>
    IReadOnlyList<string> GetProfileIds(string canonicalModelName);
}
