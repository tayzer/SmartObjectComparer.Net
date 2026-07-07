using Mapster;

namespace ParityBench.NET.ClientCustomerLookupExample;

public static class ClientCustomerLookupMapsterConfig
{
    public static TypeAdapterConfig CreateConfig()
    {
        TypeAdapterConfig config = new TypeAdapterConfig();
        Register(config);
        return config;
    }

    public static void Register(TypeAdapterConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        config.NewConfig<ClientCustomerLookupSoapRequestEnvelope, ClientCustomerLookupJsonRequest>()
            .Map(destination => destination.CustomerId, source => source.Body.LookupRequest.CustomerId)
            .Map(destination => destination.CorrelationId, source => source.Body.LookupRequest.CorrelationId);

        config.NewConfig<ClientCustomerLookupSoapResponseEnvelope, ClientCustomerLookupResponse>()
            .Map(destination => destination.ResultCode, source => source.Body.LookupResponse.StatusCode)
            .Map(destination => destination.CustomerName, source => source.Body.LookupResponse.CustomerName)
            .Map(destination => destination.TraceId, source => source.Body.LookupResponse.TraceId);

        config.NewConfig<ClientCustomerLookupJsonResponse, ClientCustomerLookupResponse>();
    }
}
