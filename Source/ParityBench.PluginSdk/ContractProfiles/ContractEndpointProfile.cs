using System.Collections.ObjectModel;

using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed record ContractEndpointProfile
{
    public ContractEndpointProfile(
        PayloadFormat requestFormat,
        string? requestContentType = null,
        PayloadFormat responseFormat = PayloadFormat.Json,
        string? suggestedEndpointId = null,
        IReadOnlyCollection<PayloadFormat>? supportedSourceRequestFormats = null,
        IReadOnlyDictionary<string, string>? requestHeaders = null)
    {
        RequestFormat = requestFormat;
        // Null means "send the source request's own content type". A profile that
        // declares one is asserting what the endpoint accepts, so it outranks the
        // content type inferred from the request file's extension.
        RequestContentType = string.IsNullOrWhiteSpace(requestContentType) ? null : requestContentType.Trim();
        ResponseFormat = responseFormat;
        SuggestedEndpointId = string.IsNullOrWhiteSpace(suggestedEndpointId) ? null : suggestedEndpointId.Trim();
        SupportedSourceRequestFormats = (supportedSourceRequestFormats ?? new[] { requestFormat }).ToArray();
        RequestHeaders = CopyHeaders(requestHeaders);
    }

    public PayloadFormat RequestFormat { get; }

    public string? RequestContentType { get; }

    public PayloadFormat ResponseFormat { get; }

    public string? SuggestedEndpointId { get; }

    public IReadOnlyCollection<PayloadFormat> SupportedSourceRequestFormats { get; }

    /// <summary>
    /// Gets the headers this contract always sends. They sit at the bottom of the
    /// outbound header precedence, so environment, profile and CLI headers — and a
    /// plugin's own request steps — still override them.
    /// </summary>
    public IReadOnlyDictionary<string, string> RequestHeaders { get; }

    private static IReadOnlyDictionary<string, string> CopyHeaders(IReadOnlyDictionary<string, string>? headers)
    {
        Dictionary<string, string> copiedHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (headers is not null)
        {
            foreach (KeyValuePair<string, string> header in headers)
            {
                if (string.IsNullOrWhiteSpace(header.Key))
                {
                    continue;
                }

                // Two spellings of the outbound content type would give one wire
                // header two sources of truth with a non-obvious winner.
                if (string.Equals(header.Key.Trim(), "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException(
                        "Declare the request content type through requestContentType, not a Content-Type header.",
                        nameof(headers));
                }

                copiedHeaders[header.Key.Trim()] = header.Value;
            }
        }

        return new ReadOnlyDictionary<string, string>(copiedHeaders);
    }
}
