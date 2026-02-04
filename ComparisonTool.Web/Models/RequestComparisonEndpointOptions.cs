namespace ComparisonTool.Web.Models;

/// <summary>
/// Configuration options for request comparison endpoints.
/// </summary>
public class RequestComparisonEndpointOptions
{
    /// <summary>Gets or sets a value indicating whether custom endpoints are allowed.</summary>
    public bool AllowCustom { get; set; } = true;

    /// <summary>Gets or sets the configured endpoint options.</summary>
    public List<RequestComparisonEndpointOption> Endpoints { get; set; } = new();
}

/// <summary>
/// Represents a named endpoint option for request comparison.
/// </summary>
public class RequestComparisonEndpointOption
{
    /// <summary>Gets or sets the display name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the endpoint URL.</summary>
    public string Url { get; set; } = string.Empty;
}
