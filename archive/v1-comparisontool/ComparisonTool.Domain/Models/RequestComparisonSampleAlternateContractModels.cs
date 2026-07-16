using System.Text.Json.Serialization;
using System.Xml.Serialization;

namespace ComparisonTool.Domain.Models;

/// <summary>
/// Namespaces used by the sample SOAP request/response models for alternate-contract request comparison.
/// </summary>
public static class SampleSoapCustomerLookupNamespaces
{
    public const string SoapEnvelope = "http://schemas.xmlsoap.org/soap/envelope/";
    public const string Service = "urn:comparisontool:sample:customer-lookup";
}

[XmlRoot("Envelope", Namespace = SampleSoapCustomerLookupNamespaces.SoapEnvelope)]
public class SampleSoapCustomerLookupRequestEnvelope
{
    [XmlElement("Body", Namespace = SampleSoapCustomerLookupNamespaces.SoapEnvelope)]
    public SampleSoapCustomerLookupRequestBody Body { get; set; } = new();
}

public class SampleSoapCustomerLookupRequestBody
{
    [XmlElement("CustomerLookupRequest", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public SampleSoapCustomerLookupRequest CustomerLookupRequest { get; set; } = new();
}

public class SampleSoapCustomerLookupRequest
{
    [XmlElement("CustomerId", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public string CustomerId { get; set; } = string.Empty;

    [XmlElement("SensitiveToken", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public string SensitiveToken { get; set; } = string.Empty;
}

[XmlRoot("Envelope", Namespace = SampleSoapCustomerLookupNamespaces.SoapEnvelope)]
public class SampleSoapCustomerLookupResponseEnvelope
{
    [XmlElement("Body", Namespace = SampleSoapCustomerLookupNamespaces.SoapEnvelope)]
    public SampleSoapCustomerLookupResponseBody Body { get; set; } = new();
}

public class SampleSoapCustomerLookupResponseBody
{
    [XmlElement("CustomerLookupResponse", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public SampleSoapCustomerLookupResponse CustomerLookupResponse { get; set; } = new();
}

public class SampleSoapCustomerLookupResponse
{
    [XmlElement("StatusCode", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public string StatusCode { get; set; } = string.Empty;

    [XmlElement("CustomerName", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public string CustomerName { get; set; } = string.Empty;

    [XmlElement("SensitiveToken", Namespace = SampleSoapCustomerLookupNamespaces.Service)]
    public string SensitiveToken { get; set; } = string.Empty;
}

public class SampleAlternateJsonCustomerLookupRequest
{
    [JsonPropertyName("lookupId")]
    public string LookupId { get; set; } = string.Empty;

    [JsonPropertyName("raw_token")]
    public string RawToken { get; set; } = string.Empty;
}

public class SampleAlternateJsonCustomerLookupResponse
{
    [JsonPropertyName("statusCode")]
    public string StatusCode { get; set; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public SampleAlternateJsonCustomerLookupPayload Payload { get; set; } = new();
}

public class SampleAlternateJsonCustomerLookupPayload
{
    [JsonPropertyName("raw_token")]
    public string RawToken { get; set; } = string.Empty;
}