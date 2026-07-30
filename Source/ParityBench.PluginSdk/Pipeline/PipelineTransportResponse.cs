using ParityBench.NET.Application.ContractProfiles;

namespace ParityBench.PluginSdk.Pipeline;

/// <summary>
/// The raw result of the transport phase, before persistence or mapping.
/// </summary>
public sealed record PipelineTransportResponse(
    int StatusCode,
    string? ContentType,
    ContractPayload Body,
    IReadOnlyDictionary<string, string>? Headers = null)
{
    public bool IsSuccessStatusCode => StatusCode >= 200 && StatusCode < 300;
}
