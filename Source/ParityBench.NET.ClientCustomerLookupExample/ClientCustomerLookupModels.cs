using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ParityBench.NET.ClientCustomerLookupExample;

[XmlRoot("Envelope")]
public sealed class ClientCustomerLookupSoapRequestEnvelope
{
    public ClientCustomerLookupSoapRequestBody Body { get; set; } = new ClientCustomerLookupSoapRequestBody();
}

public sealed class ClientCustomerLookupSoapRequestBody
{
    public ClientCustomerLookupRequest LookupRequest { get; set; } = new ClientCustomerLookupRequest();
}

public sealed class ClientCustomerLookupRequest
{
    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string CustomerId { get; set; } = string.Empty;

    public string CorrelationId { get; set; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class ClientCustomerLookupSoapResponseEnvelope
{
    public ClientCustomerLookupSoapResponseBody Body { get; set; } = new ClientCustomerLookupSoapResponseBody();
}

public sealed class ClientCustomerLookupSoapResponseBody
{
    public ClientCustomerLookupSoapResponse LookupResponse { get; set; } = new ClientCustomerLookupSoapResponse();
}

public sealed class ClientCustomerLookupSoapResponse
{
    public string StatusCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;
}

public sealed class ClientCustomerLookupJsonRequest
{
    [JsonPropertyName("customerId")]
    public string CustomerId { get; init; } = string.Empty;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; init; } = string.Empty;
}

public sealed class ClientCustomerLookupJsonResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}

public sealed class ClientCustomerLookupResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;
}
