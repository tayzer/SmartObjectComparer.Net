using Mapster;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupProfileFactory
{
    public const string ResponseModelName = "ClientCustomerLookupResponse";
    public const string ProfileId = "client.customer-lookup.soap-json.tokens.v1";
    public const string SuggestedEndpointAId = "client/customer-lookup/soap";
    public const string SuggestedEndpointBId = "client/customer-lookup/json";

    private static readonly IReadOnlyList<IgnoreRuleDefinition> BaselineIgnoreRules = new[]
    {
        new IgnoreRuleDefinition("details.traceId"),
        new IgnoreRuleDefinition("details.decisionEngine"),
        new IgnoreRuleDefinition("apps.profile.addresses", ignoreCompletely: false, ignoreCollectionOrder: true),
        new IgnoreRuleDefinition("apps.ruleEvaluations.outcomes.triggeredChecks", ignoreCompletely: false, ignoreCollectionOrder: true),
    };

    public static IContractProfile Create(
        IContractPayloadSerializer serializer,
        IClientCustomerLookupTokenProvider tokenProvider,
        TypeAdapterConfig mapsterConfig,
        ComparisonRuleDefaults? defaultComparisonRules = null)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(mapsterConfig);

        ContractPayloadFactory payloadFactory = new ContractPayloadFactory();
        ComparisonRuleDefaults effectiveComparisonRules = BuildEffectiveComparisonRules(defaultComparisonRules);

        return new ContractProfile<
            ClientCustomerLookupSoapRequestEnvelope,
            ClientCustomerLookupJsonRequest,
            ClientCustomerLookupResponse,
            ClientCustomerLookupJsonResponse>(
            serializer,
            ProfileId,
            ResponseModelName,
            request => request.Adapt<ClientCustomerLookupJsonRequest>(mapsterConfig),
            response => response.Adapt<ClientCustomerLookupResponse>(mapsterConfig),
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
            endpointBRequestFormat: PayloadFormat.Json,
            endpointBRequestContentType: "application/json",
            endpointBResponseFormat: PayloadFormat.Json,
            canonicalResponseFormat: PayloadFormat.Json,
            canonicalResponseContentType: "application/json",
            suggestedEndpointAId: SuggestedEndpointAId,
            suggestedEndpointBId: SuggestedEndpointBId,
            defaultComparisonRules: effectiveComparisonRules,
            requestPreparation: async (context, cancellationToken) =>
            {
                ClientCustomerLookupTokenResult finalToken = await tokenProvider
                    .GetFinalTokenAsync(context.SourceRequest.Body.LookupRequest, cancellationToken)
                    .ConfigureAwait(false);

                ClientCustomerLookupJsonRequest endpointBRequest = context.SourceRequest
                    .Adapt<ClientCustomerLookupJsonRequest>(mapsterConfig);

                ContractPayload body = await payloadFactory
                    .CreateAsync(
                        PayloadFormat.Json,
                        "application/json",
                        (destination, token) => serializer.SerializeAsync(
                            endpointBRequest,
                            typeof(ClientCustomerLookupJsonRequest),
                            PayloadFormat.Json,
                            destination,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new PreparedContractRequest(
                    body,
                    ProfileId,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["Authorization"] = $"Bearer {finalToken.AccessToken}",
                    });
            },
            endpointAResponseNormalizer: async (context, cancellationToken) =>
            {
                await using Stream stream = await context
                    .OpenSourceResponseBodyAsync(cancellationToken)
                    .ConfigureAwait(false);

                ClientCustomerLookupSoapResponseEnvelope soapResponse =
                    (ClientCustomerLookupSoapResponseEnvelope)await serializer
                        .DeserializeAsync(
                            typeof(ClientCustomerLookupSoapResponseEnvelope),
                            stream,
                            context.SourceFormat,
                            ignoreXmlNamespaces: true,
                            cancellationToken)
                        .ConfigureAwait(false);

                ClientCustomerLookupResponse normalized = soapResponse
                    .Adapt<ClientCustomerLookupResponse>(mapsterConfig);

                ContractPayload body = await payloadFactory
                    .CreateAsync(
                        PayloadFormat.Json,
                        "application/json",
                        (destination, token) => serializer.SerializeAsync(
                            normalized,
                            typeof(ClientCustomerLookupResponse),
                            PayloadFormat.Json,
                            destination,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new NormalizedContractResponse(body, ProfileId);
            },
            payloadFactory: payloadFactory);
    }

    private static ComparisonRuleDefaults BuildEffectiveComparisonRules(ComparisonRuleDefaults? externalDefaults)
    {
        if (externalDefaults is null)
        {
            return new ComparisonRuleDefaults(
                ignoreXmlNamespaces: true,
                ignoreRules: BaselineIgnoreRules);
        }

        IReadOnlyList<IgnoreRuleDefinition> mergedIgnoreRules = BaselineIgnoreRules
            .Concat(externalDefaults.IgnoreRules)
            .Where(rule => !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .GroupBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToList();

        return new ComparisonRuleDefaults(
            ignoreCollectionOrder: externalDefaults.IgnoreCollectionOrder,
            ignoreStringCase: externalDefaults.IgnoreStringCase,
            ignoreTrailingWhitespaceAtEnd: externalDefaults.IgnoreTrailingWhitespaceAtEnd,
            treatNullAndEmptyCollectionsAsEqual: externalDefaults.TreatNullAndEmptyCollectionsAsEqual,
            ignoreXmlNamespaces: true,
            ignoreRules: mergedIgnoreRules,
            smartIgnoreRules: externalDefaults.SmartIgnoreRules,
            maskRules: externalDefaults.MaskRules);
    }
}

