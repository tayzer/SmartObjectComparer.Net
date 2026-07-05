using ParityBench.NET.Application.AlternateContracts;
using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Infrastructure;

public sealed class AlternateContractProfile<TCanonicalRequest, TAlternateRequest, TCanonicalResponse, TAlternateResponse>
    : IAlternateContractProfile
    where TCanonicalRequest : class
    where TAlternateRequest : class
    where TCanonicalResponse : class
    where TAlternateResponse : class
{
    private readonly IContractPayloadSerializer serializer;
    private readonly FileBackedContractPayloadFactory payloadFactory;
    private readonly Func<TCanonicalRequest, TAlternateRequest> requestMapper;
    private readonly Func<TAlternateResponse, TCanonicalResponse> responseMapper;
    private readonly Func<AlternateContractRequestPreparationContext<TCanonicalRequest>, CancellationToken, ValueTask<PreparedAlternateContractRequest>>? requestPreparation;
    private readonly Func<AlternateContractResponseNormalizationContext, CancellationToken, ValueTask<NormalizedAlternateContractResponse>>? endpointAResponseNormalizer;

    public AlternateContractProfile(
        IContractPayloadSerializer serializer,
        string profileId,
        string canonicalModelName,
        Func<TCanonicalRequest, TAlternateRequest> requestMapper,
        Func<TAlternateResponse, TCanonicalResponse> responseMapper,
        IReadOnlyCollection<PayloadFormat>? supportedSourceRequestFormats = null,
        PayloadFormat alternateRequestFormat = PayloadFormat.Json,
        string alternateRequestContentType = "application/json",
        PayloadFormat alternateResponseFormat = PayloadFormat.Json,
        PayloadFormat canonicalResponseFormat = PayloadFormat.Xml,
        string canonicalResponseContentType = "application/xml",
        string? suggestedEndpointAId = null,
        string? suggestedEndpointBId = null,
        IReadOnlyList<IgnoreRuleDefinition>? defaultIgnoreRules = null,
        IReadOnlyDictionary<string, string>? canonicalToAlternateResponseMaskPathMap = null,
        Func<AlternateContractRequestPreparationContext<TCanonicalRequest>, CancellationToken, ValueTask<PreparedAlternateContractRequest>>? requestPreparation = null,
        Func<AlternateContractResponseNormalizationContext, CancellationToken, ValueTask<NormalizedAlternateContractResponse>>? endpointAResponseNormalizer = null,
        FileBackedContractPayloadFactory? payloadFactory = null)
    {
        this.serializer = serializer;
        this.payloadFactory = payloadFactory ?? new FileBackedContractPayloadFactory();
        this.requestMapper = requestMapper;
        this.responseMapper = responseMapper;
        this.requestPreparation = requestPreparation;
        this.endpointAResponseNormalizer = endpointAResponseNormalizer;

        ProfileId = string.IsNullOrWhiteSpace(profileId) ? throw new ArgumentException("Profile id must not be empty.", nameof(profileId)) : profileId.Trim();
        CanonicalModelName = string.IsNullOrWhiteSpace(canonicalModelName) ? throw new ArgumentException("Canonical model name must not be empty.", nameof(canonicalModelName)) : canonicalModelName.Trim();
        SupportedSourceRequestFormats = (supportedSourceRequestFormats ?? new[] { PayloadFormat.Xml }).ToArray();
        AlternateRequestFormat = alternateRequestFormat;
        AlternateRequestContentType = string.IsNullOrWhiteSpace(alternateRequestContentType) ? "application/json" : alternateRequestContentType;
        AlternateResponseFormat = alternateResponseFormat;
        CanonicalResponseFormat = canonicalResponseFormat;
        CanonicalResponseContentType = string.IsNullOrWhiteSpace(canonicalResponseContentType) ? "application/xml" : canonicalResponseContentType;
        SuggestedEndpointAId = string.IsNullOrWhiteSpace(suggestedEndpointAId) ? null : suggestedEndpointAId;
        SuggestedEndpointBId = string.IsNullOrWhiteSpace(suggestedEndpointBId) ? null : suggestedEndpointBId;
        DefaultIgnoreRules = (defaultIgnoreRules ?? Array.Empty<IgnoreRuleDefinition>()).ToArray();
        CanonicalToAlternateResponseMaskPathMap = new Dictionary<string, string>(
            canonicalToAlternateResponseMaskPathMap ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public string ProfileId { get; }

    public string CanonicalModelName { get; }

    public Type CanonicalRequestType => typeof(TCanonicalRequest);

    public Type AlternateRequestType => typeof(TAlternateRequest);

    public Type CanonicalResponseType => typeof(TCanonicalResponse);

    public Type AlternateResponseType => typeof(TAlternateResponse);

    public IReadOnlyCollection<PayloadFormat> SupportedSourceRequestFormats { get; }

    public PayloadFormat AlternateRequestFormat { get; }

    public string AlternateRequestContentType { get; }

    public PayloadFormat AlternateResponseFormat { get; }

    public PayloadFormat CanonicalResponseFormat { get; }

    public string CanonicalResponseContentType { get; }

    public string? SuggestedEndpointAId { get; }

    public string? SuggestedEndpointBId { get; }

    public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules { get; }

    public IReadOnlyDictionary<string, string> CanonicalToAlternateResponseMaskPathMap { get; }

    public async ValueTask<PreparedAlternateContractRequest> PrepareEndpointBRequestAsync(
        AlternateContractRequestPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        await using Stream sourceStream = await context
            .OpenSourceRequestBodyAsync(cancellationToken)
            .ConfigureAwait(false);
        TCanonicalRequest canonicalRequest = (TCanonicalRequest)await serializer
            .DeserializeAsync(typeof(TCanonicalRequest), sourceStream, context.SourceFormat, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);

        if (requestPreparation is not null)
        {
            return await requestPreparation(
                new AlternateContractRequestPreparationContext<TCanonicalRequest>(
                    context.Request,
                    context.OpenSourceRequestBodyAsync,
                    context.SourceFormat,
                    canonicalRequest),
                cancellationToken).ConfigureAwait(false);
        }

        TAlternateRequest alternateRequest = requestMapper(canonicalRequest);
        ContractPayload body = await CreatePayloadAsync(
            alternateRequest,
            typeof(TAlternateRequest),
            AlternateRequestFormat,
            AlternateRequestContentType,
            cancellationToken).ConfigureAwait(false);

        return new PreparedAlternateContractRequest(body, ProfileId);
    }

    public async ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointAResponseAsync(
        AlternateContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default)
    {
        if (endpointAResponseNormalizer is not null)
        {
            NormalizedAlternateContractResponse normalized = await endpointAResponseNormalizer(context, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(normalized.ProfileId)
                ? new NormalizedAlternateContractResponse(normalized.Body, ProfileId)
                : normalized;
        }

        await using Stream sourceStream = await context
            .OpenSourceResponseBodyAsync(cancellationToken)
            .ConfigureAwait(false);
        TCanonicalResponse canonicalResponse = (TCanonicalResponse)await serializer
            .DeserializeAsync(typeof(TCanonicalResponse), sourceStream, context.SourceFormat, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);

        return await SerializeCanonicalResponseAsync(canonicalResponse, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<NormalizedAlternateContractResponse> NormalizeEndpointBResponseAsync(
        AlternateContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default)
    {
        await using Stream sourceStream = await context
            .OpenSourceResponseBodyAsync(cancellationToken)
            .ConfigureAwait(false);
        TAlternateResponse alternateResponse = (TAlternateResponse)await serializer
            .DeserializeAsync(typeof(TAlternateResponse), sourceStream, AlternateResponseFormat, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);
        TCanonicalResponse canonicalResponse = responseMapper(alternateResponse);

        return await SerializeCanonicalResponseAsync(canonicalResponse, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NormalizedAlternateContractResponse> SerializeCanonicalResponseAsync(
        TCanonicalResponse canonicalResponse,
        CancellationToken cancellationToken)
    {
        ContractPayload body = await CreatePayloadAsync(
            canonicalResponse,
            typeof(TCanonicalResponse),
            CanonicalResponseFormat,
            CanonicalResponseContentType,
            cancellationToken).ConfigureAwait(false);

        return new NormalizedAlternateContractResponse(body, ProfileId);
    }

    private Task<ContractPayload> CreatePayloadAsync(
        object value,
        Type valueType,
        PayloadFormat format,
        string contentType,
        CancellationToken cancellationToken) =>
        payloadFactory.CreateAsync(
            format,
            contentType,
            (destination, token) => serializer.SerializeAsync(value, valueType, format, destination, token),
            cancellationToken);
}