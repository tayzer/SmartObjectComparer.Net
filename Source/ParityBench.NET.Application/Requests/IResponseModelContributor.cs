namespace ParityBench.NET.Application.Requests;

/// <summary>
/// Registers a module's response models into the shared response model registry.
/// Implementations are resolved from DI so each client module can self-register
/// without the host wiring naming it explicitly.
/// </summary>
public interface IResponseModelContributor
{
    void Register(IResponseModelRegistry registry);
}
