namespace ParityBench.NET.Application.ContractProfiles;

/// <summary>
/// Registers a module's contract profiles into the shared contract profile registry.
/// Implementations are resolved from DI so each client module can self-register
/// without the host wiring naming it explicitly.
/// </summary>
public interface IContractProfileContributor
{
    void Register(IContractProfileRegistry registry, IServiceProvider serviceProvider);
}
