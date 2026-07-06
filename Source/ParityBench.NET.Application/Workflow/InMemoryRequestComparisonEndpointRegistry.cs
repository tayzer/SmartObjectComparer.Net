namespace ParityBench.NET.Application.Workflow;

public sealed class InMemoryRequestComparisonEndpointRegistry : IRequestComparisonEndpointRegistry
{
    private readonly Dictionary<string, EndpointOption> endpointsById = new Dictionary<string, EndpointOption>(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new object();

    public void Register(EndpointOption endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);

        if (string.IsNullOrWhiteSpace(endpoint.EndpointId))
        {
            throw new ArgumentException("Endpoint id must not be empty.", nameof(endpoint));
        }

        lock (gate)
        {
            if (endpointsById.ContainsKey(endpoint.EndpointId))
            {
                throw new InvalidOperationException($"Endpoint '{endpoint.EndpointId}' is already registered.");
            }

            endpointsById[endpoint.EndpointId] = endpoint;
        }
    }

    public IReadOnlyList<EndpointOption> ListEndpoints()
    {
        lock (gate)
        {
            return endpointsById.Values
                .OrderBy(endpoint => endpoint.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(endpoint => endpoint.EndpointId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public bool TryGetEndpoint(string endpointId, out EndpointOption? endpoint)
    {
        if (string.IsNullOrWhiteSpace(endpointId))
        {
            endpoint = null;
            return false;
        }

        lock (gate)
        {
            return endpointsById.TryGetValue(endpointId, out endpoint);
        }
    }
}
