namespace ParityBench.NET.Application.Workflow;

/// <summary>
/// Provides registered request-comparison defaults for host setup screens.
/// </summary>
public interface IRequestComparisonDefaultsUseCases
{
    /// <summary>
    /// Loads model, profile, endpoint, and preset defaults registered in the current host.
    /// </summary>
    Task<RequestComparisonDefaults> LoadDefaultsAsync(CancellationToken cancellationToken = default);
}
