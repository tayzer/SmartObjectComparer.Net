using Microsoft.Extensions.Configuration;

using ParityBench.NET.Application.Workflow;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupExampleDefaults
{
    public const string PresetId = "client-soap-json-token";
    public const string VolumePresetId = "client-soap-json-token-volume";

    public static void Register(
        IRequestComparisonEndpointRegistry endpointRegistry,
        IRequestComparisonPresetRegistry presetRegistry,
        IConfiguration configuration,
        string manualRunRoot)
    {
        ArgumentNullException.ThrowIfNull(endpointRegistry);
        ArgumentNullException.ThrowIfNull(presetRegistry);
        ArgumentNullException.ThrowIfNull(configuration);

        IReadOnlyDictionary<string, ConfiguredEndpoint> endpoints = LoadConfiguredEndpoints(configuration);
        if (!endpoints.TryGetValue(ClientCustomerLookupProfileFactory.SuggestedEndpointAId, out ConfiguredEndpoint? endpointA))
        {
            throw new InvalidOperationException($"Client customer lookup Endpoint A '{ClientCustomerLookupProfileFactory.SuggestedEndpointAId}' is not configured.");
        }

        if (!endpoints.TryGetValue(ClientCustomerLookupProfileFactory.SuggestedEndpointBId, out ConfiguredEndpoint? endpointB))
        {
            throw new InvalidOperationException($"Client customer lookup Endpoint B '{ClientCustomerLookupProfileFactory.SuggestedEndpointBId}' is not configured.");
        }

        RegisterEndpoint(endpointRegistry, endpointA);
        RegisterEndpoint(endpointRegistry, endpointB);

        IConfigurationSection profileSection = configuration.GetSection("ClientCustomerLookup:Profile");
        string requestDirectory = ResolveRequestDirectory(
            profileSection["RequestDirectory"],
            manualRunRoot);

        presetRegistry.Register(new RequestComparisonPresetOption(
            profileSection["PresetId"] ?? PresetId,
            profileSection["Label"] ?? "Client SOAP/JSON token customer lookup",
            requestDirectory,
            endpointA.Url,
            endpointB.Url,
            profileSection["ResponseModel"] ?? ClientCustomerLookupProfileFactory.ResponseModelName,
            profileSection["ProfileId"] ?? ClientCustomerLookupProfileFactory.ProfileId,
            new ComparisonOptions(ignoreXmlNamespaces: true),
            new RequestExecutionOptions(),
            endpointA.DefaultHeaders,
            endpointB.DefaultHeaders));
    }

    public static void RegisterVolumePreset(
        IRequestComparisonPresetRegistry presetRegistry,
        IConfiguration configuration,
        string manualRunRoot)
    {
        ArgumentNullException.ThrowIfNull(presetRegistry);
        ArgumentNullException.ThrowIfNull(configuration);

        IReadOnlyDictionary<string, ConfiguredEndpoint> endpoints = LoadConfiguredEndpoints(configuration);
        if (!endpoints.TryGetValue(ClientCustomerLookupProfileFactory.SuggestedEndpointAId, out ConfiguredEndpoint? endpointA))
        {
            throw new InvalidOperationException($"Client customer lookup Endpoint A '{ClientCustomerLookupProfileFactory.SuggestedEndpointAId}' is not configured.");
        }

        if (!endpoints.TryGetValue(ClientCustomerLookupProfileFactory.SuggestedEndpointBId, out ConfiguredEndpoint? endpointB))
        {
            throw new InvalidOperationException($"Client customer lookup Endpoint B '{ClientCustomerLookupProfileFactory.SuggestedEndpointBId}' is not configured.");
        }

        IConfigurationSection profileSection = configuration.GetSection("ClientCustomerLookup:VolumeProfile");
        string requestDirectory = ResolveVolumeRequestDirectory(profileSection["RequestDirectory"], manualRunRoot);

        presetRegistry.Register(new RequestComparisonPresetOption(
            profileSection["PresetId"] ?? VolumePresetId,
            profileSection["Label"] ?? "Client SOAP/JSON token customer lookup (volume)",
            requestDirectory,
            endpointA.Url,
            endpointB.Url,
            profileSection["ResponseModel"] ?? ClientCustomerLookupProfileFactory.ResponseModelName,
            profileSection["ProfileId"] ?? ClientCustomerLookupProfileFactory.ProfileId,
            new ComparisonOptions(ignoreXmlNamespaces: true),
            new RequestExecutionOptions(),
            endpointA.DefaultHeaders,
            endpointB.DefaultHeaders));
    }

    private static string ResolveVolumeRequestDirectory(string? configuredPath, string manualRunRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(manualRunRoot, "client-soap-json-token", "volume");
        }

        return ResolveRequestDirectory(configuredPath, manualRunRoot);
    }

    private static string ResolveRequestDirectory(string? configuredPath, string manualRunRoot)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return Path.Combine(manualRunRoot, "client-soap-json-token", "behaviors");
        }

        string trimmed = configuredPath.Trim();
        if (Path.IsPathRooted(trimmed))
        {
            return trimmed;
        }

        string normalized = trimmed.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
        string manualRunPrefix = Path.Combine("Examples", "ParityBench.NET.ManualRuns") + Path.DirectorySeparatorChar;
        if (normalized.StartsWith(manualRunPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Path.Combine(manualRunRoot, normalized[manualRunPrefix.Length..]);
        }

        return Path.Combine(manualRunRoot, normalized);
    }

    private static void RegisterEndpoint(
        IRequestComparisonEndpointRegistry endpointRegistry,
        ConfiguredEndpoint endpoint)
    {
        endpointRegistry.Register(new EndpointOption(
            endpoint.Id,
            endpoint.Name,
            endpoint.Url,
            endpoint.DefaultHeaders));
    }

    private static IReadOnlyDictionary<string, ConfiguredEndpoint> LoadConfiguredEndpoints(IConfiguration configuration)
    {
        Dictionary<string, ConfiguredEndpoint> endpoints = new Dictionary<string, ConfiguredEndpoint>(StringComparer.OrdinalIgnoreCase);
        foreach (IConfigurationSection endpointSection in configuration.GetSection("RequestComparison:EndpointOptions:Endpoints").GetChildren())
        {
            string? id = endpointSection["Id"];
            string? name = endpointSection["Name"];
            string? url = endpointSection["Url"];
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url))
            {
                continue;
            }

            Dictionary<string, string> headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? contentType = endpointSection["ContentType"];
            if (!string.IsNullOrWhiteSpace(contentType))
            {
                headers["Content-Type"] = contentType;
            }

            foreach (IConfigurationSection headerSection in endpointSection.GetSection("DefaultHeaders").GetChildren())
            {
                if (!string.IsNullOrWhiteSpace(headerSection.Key) && !string.IsNullOrWhiteSpace(headerSection.Value))
                {
                    headers[headerSection.Key] = headerSection.Value!;
                }
            }

            endpoints[id] = new ConfiguredEndpoint(
                id,
                string.IsNullOrWhiteSpace(name) ? id : name!,
                new Uri(url, UriKind.Absolute),
                headers);
        }

        return endpoints;
    }

    private sealed record ConfiguredEndpoint(
        string Id,
        string Name,
        Uri Url,
        IReadOnlyDictionary<string, string> DefaultHeaders);
}
