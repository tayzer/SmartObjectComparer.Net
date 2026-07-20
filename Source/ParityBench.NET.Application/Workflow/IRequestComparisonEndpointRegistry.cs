namespace ParityBench.NET.Application.Workflow;

/// <summary>
/// Registers selectable endpoint defaults for request-comparison hosts.
/// </summary>
public interface IRequestComparisonEndpointRegistry
{
    /// <summary>
    /// Registers an endpoint option.
    /// </summary>
    void Register(EndpointOption endpoint);

    /// <summary>
    /// Lists all registered endpoint options.
    /// </summary>
    IReadOnlyList<EndpointOption> ListEndpoints();

    /// <summary>
    /// Attempts to resolve an endpoint option by stable endpoint id.
    /// </summary>
    bool TryGetEndpoint(string endpointId, out EndpointOption? endpoint);
}
