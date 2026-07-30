using Mapster;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Requests;

using ParityBench.PluginSdk.Pipeline;

namespace ParityBench.ClientCustomerLookupPlugin;

/// <summary>
/// Mapping phase. Projects each endpoint's persisted response onto the shared
/// canonical type: endpoint A's SOAP envelope and endpoint B's JSON response both
/// become a <see cref="ClientCustomerLookupResponse"/>, which is what
/// Compare-Net-Objects diffs. Runs before the built-in mapping step (Order 0 vs
/// 1000), which then persists the canonical projection this produced.
/// </summary>
public sealed class ClientCustomerLookupMappingMiddleware : IEndpointComparisonMiddleware
{
    public const string Id = "client.customer-lookup.mapping";

    private readonly IContractPayloadSerializer serializer;
    private readonly TypeAdapterConfig mapsterConfig;

    public ClientCustomerLookupMappingMiddleware(IContractPayloadSerializer serializer, TypeAdapterConfig mapsterConfig)
    {
        this.serializer = serializer;
        this.mapsterConfig = mapsterConfig;
    }

    public string StepId => Id;

    public PipelinePhase Phase => PipelinePhase.Mapping;

    public int Order => 0;

    public async ValueTask InvokeAsync(
        IEndpointPipelineContext context,
        PipelineDelegate next,
        CancellationToken cancellationToken)
    {
        context.ComparisonInstance = context.Endpoint == EndpointSlot.A
            ? await MapEndpointAAsync(context, cancellationToken).ConfigureAwait(false)
            : await MapEndpointBAsync(context, cancellationToken).ConfigureAwait(false);

        await next(cancellationToken).ConfigureAwait(false);
    }

    private async Task<ClientCustomerLookupResponse> MapEndpointAAsync(IEndpointPipelineContext context, CancellationToken cancellationToken)
    {
        ClientCustomerLookupSoapResponseEnvelope soap = (ClientCustomerLookupSoapResponseEnvelope)await DeserializeAsync(
            context,
            typeof(ClientCustomerLookupSoapResponseEnvelope),
            PayloadFormat.Xml,
            cancellationToken).ConfigureAwait(false);
        return soap.Adapt<ClientCustomerLookupResponse>(mapsterConfig);
    }

    private async Task<ClientCustomerLookupResponse> MapEndpointBAsync(IEndpointPipelineContext context, CancellationToken cancellationToken)
    {
        ClientCustomerLookupJsonResponse json = (ClientCustomerLookupJsonResponse)await DeserializeAsync(
            context,
            typeof(ClientCustomerLookupJsonResponse),
            PayloadFormat.Json,
            cancellationToken).ConfigureAwait(false);
        return json.Adapt<ClientCustomerLookupResponse>(mapsterConfig);
    }

    private async Task<object> DeserializeAsync(
        IEndpointPipelineContext context,
        Type targetType,
        PayloadFormat format,
        CancellationToken cancellationToken)
    {
        await using Stream body = await context.OpenResponseArtifactAsync(cancellationToken).ConfigureAwait(false);
        return await serializer
            .DeserializeAsync(targetType, body, format, ignoreXmlNamespaces: true, cancellationToken)
            .ConfigureAwait(false);
    }
}
