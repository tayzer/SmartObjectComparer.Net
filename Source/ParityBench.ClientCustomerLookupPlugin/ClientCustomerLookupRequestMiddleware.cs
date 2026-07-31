using Mapster;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;
using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.ClientCustomerLookupPlugin;

/// <summary>
/// Request phase. Endpoint A keeps the source SOAP untouched — its content type and
/// SOAPAction header are declared on the comparison, so there is nothing to do here;
/// endpoint B exchanges a bearer token, maps the SOAP request to the endpoint's JSON
/// contract, and attaches the auth headers it requires.
/// </summary>
public sealed class ClientCustomerLookupRequestMiddleware : IEndpointComparisonMiddleware
{
    public const string Id = "client.customer-lookup.request";

    private readonly IContractPayloadSerializer serializer;
    private readonly TypeAdapterConfig mapsterConfig;
    private readonly ClientCustomerLookupTokenExchange tokenExchange;

    public ClientCustomerLookupRequestMiddleware(
        IContractPayloadSerializer serializer,
        TypeAdapterConfig mapsterConfig,
        ClientCustomerLookupTokenExchange tokenExchange)
    {
        this.serializer = serializer;
        this.mapsterConfig = mapsterConfig;
        this.tokenExchange = tokenExchange;
    }

    public string StepId => Id;

    public PipelinePhase Phase => PipelinePhase.Request;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        IStepConfiguration configuration = context.Configuration.ForStep(Id);

        if (context.Endpoint == EndpointSlot.A)
        {
            // Nothing to add: endpoint A posts the source SOAP as-is, and the token
            // exchange below is endpoint B's requirement alone.
            await next(cancellationToken).ConfigureAwait(false);
            return;
        }

        ClientCustomerLookupSoapRequestEnvelope sourceRequest = await ReadSourceRequestAsync(context, cancellationToken).ConfigureAwait(false);

        ClientCustomerLookupTokenResult token = await tokenExchange
            .GetFinalTokenAsync(sourceRequest.Body.LookupRequest, ReadTokenOptions(configuration), cancellationToken)
            .ConfigureAwait(false);

        ClientCustomerLookupJsonRequest endpointBRequest = sourceRequest.Adapt<ClientCustomerLookupJsonRequest>(mapsterConfig);
        context.RequestBody = await SerializeAsync(endpointBRequest, cancellationToken).ConfigureAwait(false);

        context.RequestHeaders[ClientCustomerLookupTokenExchange.SubscriptionKeyHeaderName] =
            configuration.GetRequiredString("endpointBSubscriptionKey");
        context.RequestHeaders["Authorization"] = $"Bearer {token.AccessToken}";

        await next(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientCustomerLookupSoapRequestEnvelope> ReadSourceRequestAsync(
        IEndpointPipelineContext context,
        CancellationToken cancellationToken)
    {
        await using Stream body = await context.OpenSourceRequestBodyAsync(cancellationToken).ConfigureAwait(false);
        return (ClientCustomerLookupSoapRequestEnvelope)await serializer
            .DeserializeAsync(typeof(ClientCustomerLookupSoapRequestEnvelope), body, PayloadFormat.Xml, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ContractPayload> SerializeAsync(ClientCustomerLookupJsonRequest request, CancellationToken cancellationToken)
    {
        await using MemoryStream buffer = new MemoryStream();
        await serializer.SerializeAsync(request, typeof(ClientCustomerLookupJsonRequest), PayloadFormat.Json, buffer, cancellationToken).ConfigureAwait(false);
        return ContractPayload.FromBytes(buffer.ToArray(), PayloadFormat.Json, "application/json");
    }

    private static ClientCustomerLookupTokenOptions ReadTokenOptions(IStepConfiguration configuration) =>
        new ClientCustomerLookupTokenOptions(
            configuration.GetRequiredString("primaryTokenUrl"),
            configuration.GetRequiredString("primaryTokenSubscriptionKey"),
            configuration.GetRequiredString("finalTokenUrl"),
            configuration.GetRequiredString("finalTokenSubscriptionKey"),
            configuration.GetRequiredString("endpointBSubscriptionKey"));
}
