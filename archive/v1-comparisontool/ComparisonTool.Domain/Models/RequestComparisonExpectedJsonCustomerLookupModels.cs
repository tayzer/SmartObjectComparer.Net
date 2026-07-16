using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ComparisonTool.Domain.Models;

[XmlRoot("Envelope")]
public class ExpectedJsonCustomerLookupSoapRequestEnvelope
{
    public ExpectedJsonCustomerLookupSoapRequestBody Body { get; set; } = new();
}

public class ExpectedJsonCustomerLookupSoapRequestBody
{
    public ExpectedJsonCustomerLookupSoapRequest CustomerLookupRequest { get; set; } = new();
}

public class ExpectedJsonCustomerLookupSoapRequest
{
    public string CustomerId { get; set; } = string.Empty;

    public string AuthenticationToken { get; set; } = string.Empty;
}

[XmlRoot("Envelope")]
public class ExpectedJsonCustomerLookupSoapResponseEnvelope
{
    public ExpectedJsonCustomerLookupSoapResponseBody Body { get; set; } = new();
}

public class ExpectedJsonCustomerLookupSoapResponseBody
{
    public ExpectedJsonCustomerLookupSoapResponse CustomerLookupResponse { get; set; } = new();
}

public class ExpectedJsonCustomerLookupSoapResponse
{
    public string StatusCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;
}

public class ExpectedJsonCustomerLookupAuthorizationTokenRequest
{
    [JsonPropertyName("customerId")]
    public string CustomerId { get; set; } = string.Empty;

    [JsonPropertyName("authenticationToken")]
    public string AuthenticationToken { get; set; } = string.Empty;
}

public class ExpectedJsonCustomerLookupAuthorizationTokenResponse
{
    [JsonPropertyName("authorizationToken")]
    public string AuthorizationToken { get; set; } = string.Empty;

    [JsonPropertyName("backupAuthorizationToken")]
    public string BackupAuthorizationToken { get; set; } = string.Empty;
}

public class ExpectedJsonCustomerLookupAlternateRequest
{
    [JsonPropertyName("lookupId")]
    public string LookupId { get; set; } = string.Empty;
}

public class ExpectedJsonCustomerLookupAlternateResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; set; } = string.Empty;
}

public class ExpectedJsonCustomerLookupResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; set; } = string.Empty;

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; set; } = string.Empty;
}