using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record ContractEndpointProfile
{
    public ContractEndpointProfile(
        PayloadFormat requestFormat,
        string requestContentType,
        PayloadFormat responseFormat,
        string? suggestedEndpointId = null,
        IReadOnlyCollection<PayloadFormat>? supportedSourceRequestFormats = null)
    {
        RequestFormat = requestFormat;
        RequestContentType = string.IsNullOrWhiteSpace(requestContentType) ? "application/octet-stream" : requestContentType.Trim();
        ResponseFormat = responseFormat;
        SuggestedEndpointId = string.IsNullOrWhiteSpace(suggestedEndpointId) ? null : suggestedEndpointId.Trim();
        SupportedSourceRequestFormats = (supportedSourceRequestFormats ?? new[] { requestFormat }).ToArray();
    }

    public PayloadFormat RequestFormat { get; }

    public string RequestContentType { get; }

    public PayloadFormat ResponseFormat { get; }

    public string? SuggestedEndpointId { get; }

    public IReadOnlyCollection<PayloadFormat> SupportedSourceRequestFormats { get; }
}
