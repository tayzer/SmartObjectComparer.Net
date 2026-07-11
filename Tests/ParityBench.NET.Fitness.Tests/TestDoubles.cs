using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Fitness.Tests;

internal sealed class EmptyResponseModelRegistry : IResponseModelRegistry
{
    public void Register<T>(string modelName) where T : class
    {
    }

    public Type Resolve(string modelName) => throw new KeyNotFoundException(modelName);

    public IReadOnlyList<string> ListModelNames() => Array.Empty<string>();
}

internal sealed class EmptyContractProfileRegistry : IContractProfileRegistry
{
    public void Register(IContractProfile profile)
    {
    }

    public IContractProfile Resolve(string responseModelName, ContractProfileSelection? selection = null) =>
        throw new KeyNotFoundException(responseModelName);

    public bool TryResolve(
        string responseModelName,
        ContractProfileSelection? selection,
        out IContractProfile? profile,
        out string? errorMessage)
    {
        profile = null;
        errorMessage = $"No contract profiles are registered for response model '{responseModelName}'.";
        return false;
    }

    public IReadOnlyList<string> GetProfileIds(string responseModelName) => Array.Empty<string>();
}
