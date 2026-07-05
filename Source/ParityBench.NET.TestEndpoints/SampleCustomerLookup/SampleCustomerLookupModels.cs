using System.Text.Json.Serialization;

namespace ParityBench.NET.TestEndpoints.SampleCustomerLookup;

public sealed class SampleCustomerLookupRequest
{
    public string CustomerId { get; set; } = string.Empty;

    public string SensitiveToken { get; set; } = string.Empty;
}

public sealed class SampleCustomerLookupSoapResponse
{
    public string StatusCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string SensitiveToken { get; set; } = string.Empty;
}

public sealed class SampleCustomerLookupJsonRequest
{
    [JsonPropertyName("lookupId")]
    public string LookupId { get; set; } = string.Empty;

    [JsonPropertyName("raw_token")]
    public string RawToken { get; set; } = string.Empty;
}

public sealed class SampleCustomerLookupJsonResponse
{
    [JsonPropertyName("statusCode")]
    public string StatusCode { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public SampleCustomerLookupPayload Payload { get; set; } = new SampleCustomerLookupPayload();
}

public sealed class SampleCustomerLookupPayload
{
    [JsonPropertyName("raw_token")]
    public string RawToken { get; set; } = string.Empty;
}
