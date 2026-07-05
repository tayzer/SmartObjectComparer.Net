using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

/// <summary>
/// Registers and resolves selectable contract profiles.
/// </summary>
public interface IContractProfileRegistry
{
    /// <summary>
    /// Registers a contract profile.
    /// </summary>
    void Register(IContractProfile profile);

    /// <summary>
    /// Resolves the selected profile for a response model.
    /// </summary>
    IContractProfile Resolve(string responseModelName, ContractProfileSelection? selection = null);

    /// <summary>
    /// Attempts to resolve the selected profile for a response model.
    /// </summary>
    bool TryResolve(
        string responseModelName,
        ContractProfileSelection? selection,
        out IContractProfile? profile,
        out string? errorMessage);

    /// <summary>
    /// Gets profile ids registered for a response model.
    /// </summary>
    IReadOnlyList<string> GetProfileIds(string responseModelName);
}
