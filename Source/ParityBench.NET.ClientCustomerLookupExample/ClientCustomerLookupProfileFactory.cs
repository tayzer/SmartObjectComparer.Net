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

    public static IContractProfile Create(
        IContractPayloadSerializer serializer,
        IClientCustomerLookupTokenProvider tokenProvider,
        TypeAdapterConfig mapsterConfig)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(tokenProvider);
        ArgumentNullException.ThrowIfNull(mapsterConfig);

        ContractPayloadFactory payloadFactory = new ContractPayloadFactory();

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
            defaultComparisonRules: new ComparisonRuleDefaults(ignoreXmlNamespaces: true),
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
}
