using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Infrastructure;

public sealed class ContractProfile<TEndpointARequest, TEndpointBRequest, TCanonicalResponse, TEndpointBResponse>
    : IContractProfile
    where TEndpointARequest : class
    where TEndpointBRequest : class
    where TCanonicalResponse : class
    where TEndpointBResponse : class
{
    private readonly IContractPayloadSerializer serializer;
    private readonly ContractPayloadFactory payloadFactory;
    private readonly Func<TEndpointARequest, TEndpointBRequest> requestMapper;
    private readonly Func<TEndpointBResponse, TCanonicalResponse> responseMapper;
    private readonly Func<ContractRequestPreparationContext<TEndpointARequest>, CancellationToken, ValueTask<PreparedContractRequest>>? requestPreparation;
    private readonly Func<ContractResponseNormalizationContext, CancellationToken, ValueTask<NormalizedContractResponse>>? endpointAResponseNormalizer;

    public ContractProfile(
        IContractPayloadSerializer serializer,
        string profileId,
        string responseModelName,
        Func<TEndpointARequest, TEndpointBRequest> requestMapper,
        Func<TEndpointBResponse, TCanonicalResponse> responseMapper,
        IReadOnlyCollection<PayloadFormat>? supportedSourceRequestFormats = null,
        PayloadFormat endpointBRequestFormat = PayloadFormat.Json,
        string endpointBRequestContentType = "application/json",
        PayloadFormat endpointBResponseFormat = PayloadFormat.Json,
        PayloadFormat canonicalResponseFormat = PayloadFormat.Xml,
        string canonicalResponseContentType = "application/xml",
        string? suggestedEndpointAId = null,
        string? suggestedEndpointBId = null,
        IReadOnlyList<IgnoreRuleDefinition>? defaultIgnoreRules = null,
        ComparisonRuleDefaults? defaultComparisonRules = null,
        IReadOnlyDictionary<string, string>? canonicalToEndpointResponseMaskPathMap = null,
        Func<ContractRequestPreparationContext<TEndpointARequest>, CancellationToken, ValueTask<PreparedContractRequest>>? requestPreparation = null,
        Func<ContractResponseNormalizationContext, CancellationToken, ValueTask<NormalizedContractResponse>>? endpointAResponseNormalizer = null,
        ContractPayloadFactory? payloadFactory = null)
    {
        this.serializer = serializer;
        this.payloadFactory = payloadFactory ?? new ContractPayloadFactory();
        this.requestMapper = requestMapper;
        this.responseMapper = responseMapper;
        this.requestPreparation = requestPreparation;
        this.endpointAResponseNormalizer = endpointAResponseNormalizer;

        ProfileId = string.IsNullOrWhiteSpace(profileId) ? throw new ArgumentException("Profile id must not be empty.", nameof(profileId)) : profileId.Trim();
        ResponseModelName = string.IsNullOrWhiteSpace(responseModelName) ? throw new ArgumentException("Response model name must not be empty.", nameof(responseModelName)) : responseModelName.Trim();
        ProfileVersion = "1";
        IReadOnlyCollection<PayloadFormat> sourceFormats = (supportedSourceRequestFormats ?? new[] { PayloadFormat.Xml }).ToArray();
        EndpointA = new ContractEndpointProfile(sourceFormats.First(), canonicalResponseContentType, canonicalResponseFormat, suggestedEndpointAId, sourceFormats);
        EndpointB = new ContractEndpointProfile(endpointBRequestFormat, endpointBRequestContentType, endpointBResponseFormat, suggestedEndpointBId);
        CanonicalResponseFormat = canonicalResponseFormat;
        CanonicalResponseContentType = string.IsNullOrWhiteSpace(canonicalResponseContentType) ? "application/xml" : canonicalResponseContentType.Trim();
        DefaultComparisonRules = CreateDefaultComparisonRules(defaultComparisonRules, defaultIgnoreRules);
        CanonicalToEndpointResponseMaskPathMap = new Dictionary<string, string>(
            canonicalToEndpointResponseMaskPathMap ?? new Dictionary<string, string>(),
            StringComparer.OrdinalIgnoreCase);
    }

    public string ProfileId { get; }

    public string ResponseModelName { get; }

    public string? ProfileVersion { get; }

    public Type EndpointARequestType => typeof(TEndpointARequest);

    public Type EndpointBRequestType => typeof(TEndpointBRequest);

    public Type CanonicalResponseType => typeof(TCanonicalResponse);

    public Type EndpointBResponseType => typeof(TEndpointBResponse);

    public ContractEndpointProfile EndpointA { get; }

    public ContractEndpointProfile EndpointB { get; }

    public PayloadFormat CanonicalResponseFormat { get; }

    public string CanonicalResponseContentType { get; }

    public IReadOnlyList<IgnoreRuleDefinition> DefaultIgnoreRules => DefaultComparisonRules.IgnoreRules;

    public ComparisonRuleDefaults DefaultComparisonRules { get; }

    public IReadOnlyDictionary<string, string> CanonicalToEndpointResponseMaskPathMap { get; }

    public async ValueTask<PreparedContractRequest> PrepareRequestAsync(
        EndpointSlot endpoint,
        ContractRequestPreparationContext context,
        CancellationToken cancellationToken = default)
    {
        if (endpoint == EndpointSlot.A)
        {
            ContractPayload sourcePayload = new ContractPayload(
                context.SourceFormat,
                context.SourceContentType,
                context.OpenSourceRequestBodyAsync,
                context.Request.ContentLength);

            return new PreparedContractRequest(sourcePayload, ProfileId);
        }

        await using Stream sourceStream = await context
            .OpenSourceRequestBodyAsync(cancellationToken)
            .ConfigureAwait(false);
        TEndpointARequest endpointARequest = (TEndpointARequest)await serializer
            .DeserializeAsync(typeof(TEndpointARequest), sourceStream, context.SourceFormat, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);

        if (requestPreparation is not null)
        {
            return await requestPreparation(
                new ContractRequestPreparationContext<TEndpointARequest>(
                    context.Request,
                    context.OpenSourceRequestBodyAsync,
                    context.SourceFormat,
                    context.SourceContentType,
                    endpointARequest),
                cancellationToken).ConfigureAwait(false);
        }

        TEndpointBRequest endpointBRequest = requestMapper(endpointARequest);
        ContractPayload body = await CreatePayloadAsync(
            endpointBRequest,
            typeof(TEndpointBRequest),
            EndpointB.RequestFormat,
            EndpointB.RequestContentType,
            cancellationToken).ConfigureAwait(false);

        return new PreparedContractRequest(body, ProfileId);
    }

    public async ValueTask<NormalizedContractResponse> NormalizeResponseAsync(
        EndpointSlot endpoint,
        ContractResponseNormalizationContext context,
        CancellationToken cancellationToken = default)
    {
        if (endpoint == EndpointSlot.A)
        {
            return await NormalizeEndpointAResponseAsync(context, cancellationToken).ConfigureAwait(false);
        }

        return await NormalizeEndpointBResponseAsync(context, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NormalizedContractResponse> NormalizeEndpointAResponseAsync(
        ContractResponseNormalizationContext context,
        CancellationToken cancellationToken)
    {
        if (endpointAResponseNormalizer is not null)
        {
            NormalizedContractResponse normalized = await endpointAResponseNormalizer(context, cancellationToken)
                .ConfigureAwait(false);

            return string.IsNullOrWhiteSpace(normalized.ProfileId)
                ? new NormalizedContractResponse(normalized.Body, ProfileId)
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

    private async ValueTask<NormalizedContractResponse> NormalizeEndpointBResponseAsync(
        ContractResponseNormalizationContext context,
        CancellationToken cancellationToken)
    {
        await using Stream sourceStream = await context
            .OpenSourceResponseBodyAsync(cancellationToken)
            .ConfigureAwait(false);
        TEndpointBResponse endpointBResponse = (TEndpointBResponse)await serializer
            .DeserializeAsync(typeof(TEndpointBResponse), sourceStream, EndpointB.ResponseFormat, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);
        TCanonicalResponse canonicalResponse = responseMapper(endpointBResponse);

        return await SerializeCanonicalResponseAsync(canonicalResponse, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<NormalizedContractResponse> SerializeCanonicalResponseAsync(
        TCanonicalResponse canonicalResponse,
        CancellationToken cancellationToken)
    {
        ContractPayload body = await CreatePayloadAsync(
            canonicalResponse,
            typeof(TCanonicalResponse),
            CanonicalResponseFormat,
            CanonicalResponseContentType,
            cancellationToken).ConfigureAwait(false);

        return new NormalizedContractResponse(body, ProfileId);
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

    private static ComparisonRuleDefaults CreateDefaultComparisonRules(
        ComparisonRuleDefaults? defaultComparisonRules,
        IReadOnlyList<IgnoreRuleDefinition>? defaultIgnoreRules)
    {
        IReadOnlyList<IgnoreRuleDefinition> legacyIgnoreRules = defaultIgnoreRules ?? Array.Empty<IgnoreRuleDefinition>();
        if (defaultComparisonRules is null)
        {
            return new ComparisonRuleDefaults(ignoreRules: legacyIgnoreRules);
        }

        return new ComparisonRuleDefaults(
            defaultComparisonRules.IgnoreCollectionOrder,
            defaultComparisonRules.IgnoreStringCase,
            defaultComparisonRules.IgnoreTrailingWhitespaceAtEnd,
            defaultComparisonRules.TreatNullAndEmptyCollectionsAsEqual,
            defaultComparisonRules.IgnoreXmlNamespaces,
            defaultComparisonRules.IgnoreRules.Concat(legacyIgnoreRules),
            defaultComparisonRules.SmartIgnoreRules,
            defaultComparisonRules.MaskRules);
    }
}
