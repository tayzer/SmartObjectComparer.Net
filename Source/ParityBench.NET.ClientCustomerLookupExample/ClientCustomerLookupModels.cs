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
    [JsonPropertyName("details")]
    public ClientCustomerLookupDetails Details { get; init; } = new ClientCustomerLookupDetails();

    [JsonPropertyName("apps")]
    public ClientCustomerLookupApplicant[] Applicants { get; init; } = Array.Empty<ClientCustomerLookupApplicant>();

    [JsonIgnore]
    public bool IsAThing { get; init; }
}

public sealed class ClientCustomerLookupResponse
{
    [JsonPropertyName("details")]
    public ClientCustomerLookupDetails Details { get; init; } = new ClientCustomerLookupDetails();

    [JsonPropertyName("apps")]
    public ClientCustomerLookupApplicant[] Applicants { get; init; } = Array.Empty<ClientCustomerLookupApplicant>();

    [JsonIgnore]
    public bool IsAThing { get; init; }
}

public sealed class ClientCustomerLookupDetails
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("decisionEngine")]
    public string DecisionEngine { get; init; } = string.Empty;
}

public sealed class ClientCustomerLookupApplicant
{
    [JsonPropertyName("applicantId")]
    public string ApplicantId { get; init; } = string.Empty;

    [JsonPropertyName("profile")]
    public ClientCustomerLookupApplicantProfile Profile { get; init; } = new ClientCustomerLookupApplicantProfile();

    [JsonPropertyName("ruleEvaluations")]
    public ClientCustomerLookupRuleEvaluation[] RuleEvaluations { get; init; } = Array.Empty<ClientCustomerLookupRuleEvaluation>();

    [JsonPropertyName("flags")]
    public string[] Flags { get; init; } = Array.Empty<string>();
}

public sealed class ClientCustomerLookupApplicantProfile
{
    [JsonPropertyName("fullName")]
    public string FullName { get; init; } = string.Empty;

    [JsonPropertyName("addresses")]
    public ClientCustomerLookupAddress[] Addresses { get; init; } = Array.Empty<ClientCustomerLookupAddress>();
}

public sealed class ClientCustomerLookupAddress
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = string.Empty;

    [JsonPropertyName("city")]
    public string City { get; init; } = string.Empty;

    [JsonPropertyName("country")]
    public string Country { get; init; } = string.Empty;
}

public sealed class ClientCustomerLookupRuleEvaluation
{
    [JsonPropertyName("ruleSet")]
    public string RuleSet { get; init; } = string.Empty;

    [JsonPropertyName("outcomes")]
    public ClientCustomerLookupRuleOutcome[] Outcomes { get; init; } = Array.Empty<ClientCustomerLookupRuleOutcome>();
}

public sealed class ClientCustomerLookupRuleOutcome
{
    [JsonPropertyName("code")]
    public string Code { get; init; } = string.Empty;

    [JsonPropertyName("result")]
    public string Result { get; init; } = string.Empty;

    [JsonPropertyName("triggeredChecks")]
    public string[] TriggeredChecks { get; init; } = Array.Empty<string>();
}
