using System.Text.Json.Serialization;
using System.Xml.Serialization;

using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Infrastructure;

public static class BuiltInContractProfiles
{
    public const string SampleModelName = "SampleSoapCustomerLookupResponseEnvelope";
    public const string SampleProfileId = "sample-soap-to-json";
    public const string ExpectedJsonCustomerLookupModelName = "ExpectedJsonCustomerLookupResponse";
    public const string ExpectedJsonCustomerLookupProfileId = "expected-json-customer-lookup";

    public static IContractProfile CreateSampleSoapToJson(IContractPayloadSerializer serializer) =>
        new ContractProfile<
            SampleSoapCustomerLookupRequestEnvelope,
            SampleAlternateJsonCustomerLookupRequest,
            SampleSoapCustomerLookupResponseEnvelope,
            SampleAlternateJsonCustomerLookupResponse>(
            serializer,
            SampleProfileId,
            SampleModelName,
            request => new SampleAlternateJsonCustomerLookupRequest
            {
                LookupId = request.Body.CustomerLookupRequest.CustomerId,
                RawToken = request.Body.CustomerLookupRequest.SensitiveToken,
            },
            response => new SampleSoapCustomerLookupResponseEnvelope
            {
                Body = new SampleSoapCustomerLookupResponseBody
                {
                    CustomerLookupResponse = new SampleSoapCustomerLookupResponse
                    {
                        StatusCode = response.StatusCode,
                        CustomerName = response.CustomerName,
                        SensitiveToken = response.Payload.RawToken,
                    },
                },
            },
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
            endpointBRequestFormat: PayloadFormat.Json,
            endpointBRequestContentType: "application/json",
            endpointBResponseFormat: PayloadFormat.Json,
            canonicalResponseFormat: PayloadFormat.Xml,
            canonicalResponseContentType: "application/xml",
            suggestedEndpointAId: "sample/customer-lookup/soap",
            suggestedEndpointBId: "sample/customer-lookup/json",
            canonicalToEndpointResponseMaskPathMap: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Envelope.Body.CustomerLookupResponse.SensitiveToken"] = "payload.raw_token",
            });

    public static IContractProfile CreateExpectedJsonCustomerLookup(
        IContractPayloadSerializer serializer,
        IExpectedJsonCustomerLookupAuthorizationTokenProvider tokenProvider)
    {
        ContractPayloadFactory payloadFactory = new ContractPayloadFactory();

        return new ContractProfile<
            ExpectedJsonCustomerLookupSoapRequestEnvelope,
            ExpectedJsonCustomerLookupAlternateRequest,
            ExpectedJsonCustomerLookupResponse,
            ExpectedJsonCustomerLookupAlternateResponse>(
            serializer,
            ExpectedJsonCustomerLookupProfileId,
            ExpectedJsonCustomerLookupModelName,
            request => new ExpectedJsonCustomerLookupAlternateRequest
            {
                LookupId = request.Body.CustomerLookupRequest.CustomerId,
            },
            response => new ExpectedJsonCustomerLookupResponse
            {
                ResultCode = response.ResultCode,
                CustomerName = response.CustomerName,
                TraceId = response.TraceId,
                SourceSystem = response.SourceSystem,
            },
            supportedSourceRequestFormats: new[] { PayloadFormat.Xml },
            endpointBRequestFormat: PayloadFormat.Json,
            endpointBRequestContentType: "application/json",
            endpointBResponseFormat: PayloadFormat.Json,
            canonicalResponseFormat: PayloadFormat.Json,
            canonicalResponseContentType: "application/json",
            suggestedEndpointAId: "customer-lookup/soap",
            suggestedEndpointBId: "customer-lookup/json",
            defaultIgnoreRules: new[] { new IgnoreRuleDefinition("SourceSystem") },
            requestPreparation: async (context, cancellationToken) =>
            {
                ExpectedJsonCustomerLookupAuthorizationTokenResponse tokens = await tokenProvider
                    .GetAuthorizationTokensAsync(
                        new ExpectedJsonCustomerLookupAuthorizationTokenRequest
                        {
                            CustomerId = context.SourceRequest.Body.CustomerLookupRequest.CustomerId,
                            AuthenticationToken = context.SourceRequest.Body.CustomerLookupRequest.AuthenticationToken,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);

                ExpectedJsonCustomerLookupAlternateRequest alternateRequest = new ExpectedJsonCustomerLookupAlternateRequest
                {
                    LookupId = context.SourceRequest.Body.CustomerLookupRequest.CustomerId,
                };
                ContractPayload body = await payloadFactory
                    .CreateAsync(
                        PayloadFormat.Json,
                        "application/json",
                        (destination, token) => serializer.SerializeAsync(
                            alternateRequest,
                            typeof(ExpectedJsonCustomerLookupAlternateRequest),
                            PayloadFormat.Json,
                            destination,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new PreparedContractRequest(
                    body,
                    ExpectedJsonCustomerLookupProfileId,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["AuthorizationToken"] = tokens.AuthorizationToken,
                    });
            },
            endpointAResponseNormalizer: async (context, cancellationToken) =>
            {
                await using Stream stream = await context
                    .OpenSourceResponseBodyAsync(cancellationToken)
                    .ConfigureAwait(false);
                ExpectedJsonCustomerLookupSoapResponseEnvelope soapResponse =
                    (ExpectedJsonCustomerLookupSoapResponseEnvelope)await serializer
                        .DeserializeAsync(
                            typeof(ExpectedJsonCustomerLookupSoapResponseEnvelope),
                            stream,
                            context.SourceFormat,
                            ignoreXmlNamespaces: true,
                            cancellationToken)
                        .ConfigureAwait(false);

                ExpectedJsonCustomerLookupResponse normalized = new ExpectedJsonCustomerLookupResponse
                {
                    ResultCode = soapResponse.Body.CustomerLookupResponse.StatusCode,
                    CustomerName = soapResponse.Body.CustomerLookupResponse.CustomerName,
                    TraceId = soapResponse.Body.CustomerLookupResponse.TraceId,
                    SourceSystem = "endpoint-a",
                };

                ContractPayload body = await payloadFactory
                    .CreateAsync(
                        PayloadFormat.Json,
                        "application/json",
                        (destination, token) => serializer.SerializeAsync(
                            normalized,
                            typeof(ExpectedJsonCustomerLookupResponse),
                            PayloadFormat.Json,
                            destination,
                            token),
                        cancellationToken)
                    .ConfigureAwait(false);

                return new NormalizedContractResponse(
                    body,
                    ExpectedJsonCustomerLookupProfileId);
            },
            payloadFactory: payloadFactory);
    }
}

/// <summary>
/// Supplies endpoint-B authorization tokens for the expected JSON customer lookup profile.
/// </summary>
public interface IExpectedJsonCustomerLookupAuthorizationTokenProvider
{
    /// <summary>
    /// Gets authorization tokens for a transformed customer lookup request.
    /// </summary>
    Task<ExpectedJsonCustomerLookupAuthorizationTokenResponse> GetAuthorizationTokensAsync(
        ExpectedJsonCustomerLookupAuthorizationTokenRequest request,
        CancellationToken cancellationToken = default);
}

public sealed class ExpectedJsonCustomerLookupAuthorizationTokenRequest
{
    public string? CustomerId { get; init; }

    public string? AuthenticationToken { get; init; }
}

public sealed class ExpectedJsonCustomerLookupAuthorizationTokenResponse
{
    public string AuthorizationToken { get; init; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class SampleSoapCustomerLookupRequestEnvelope
{
    public SampleSoapCustomerLookupRequestBody Body { get; set; } = new SampleSoapCustomerLookupRequestBody();
}

public sealed class SampleSoapCustomerLookupRequestBody
{
    public SampleSoapCustomerLookupRequest CustomerLookupRequest { get; set; } = new SampleSoapCustomerLookupRequest();
}

public sealed class SampleSoapCustomerLookupRequest
{
    public string CustomerId { get; set; } = string.Empty;

    public string SensitiveToken { get; set; } = string.Empty;
}

public sealed class SampleAlternateJsonCustomerLookupRequest
{
    [JsonPropertyName("lookupId")]
    public string LookupId { get; init; } = string.Empty;

    [JsonPropertyName("raw_token")]
    public string RawToken { get; init; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class SampleSoapCustomerLookupResponseEnvelope
{
    public SampleSoapCustomerLookupResponseBody Body { get; set; } = new SampleSoapCustomerLookupResponseBody();
}

public sealed class SampleSoapCustomerLookupResponseBody
{
    public SampleSoapCustomerLookupResponse CustomerLookupResponse { get; set; } = new SampleSoapCustomerLookupResponse();
}

public sealed class SampleSoapCustomerLookupResponse
{
    public string StatusCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string SensitiveToken { get; set; } = string.Empty;
}

public sealed class SampleAlternateJsonCustomerLookupResponse
{
    [JsonPropertyName("statusCode")]
    public string StatusCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("payload")]
    public SampleAlternateJsonCustomerLookupPayload Payload { get; init; } = new SampleAlternateJsonCustomerLookupPayload();
}

public sealed class SampleAlternateJsonCustomerLookupPayload
{
    [JsonPropertyName("raw_token")]
    public string RawToken { get; init; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class ExpectedJsonCustomerLookupSoapRequestEnvelope
{
    public ExpectedJsonCustomerLookupSoapRequestBody Body { get; set; } = new ExpectedJsonCustomerLookupSoapRequestBody();
}

public sealed class ExpectedJsonCustomerLookupSoapRequestBody
{
    public ExpectedJsonCustomerLookupSoapRequest CustomerLookupRequest { get; set; } = new ExpectedJsonCustomerLookupSoapRequest();
}

public sealed class ExpectedJsonCustomerLookupSoapRequest
{
    public string CustomerId { get; set; } = string.Empty;

    public string AuthenticationToken { get; set; } = string.Empty;
}

public sealed class ExpectedJsonCustomerLookupAlternateRequest
{
    [JsonPropertyName("lookupId")]
    public string LookupId { get; init; } = string.Empty;
}

public sealed class ExpectedJsonCustomerLookupResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; init; } = string.Empty;
}

public sealed class ExpectedJsonCustomerLookupAlternateResponse
{
    [JsonPropertyName("resultCode")]
    public string ResultCode { get; init; } = string.Empty;

    [JsonPropertyName("customerName")]
    public string CustomerName { get; init; } = string.Empty;

    [JsonPropertyName("traceId")]
    public string TraceId { get; init; } = string.Empty;

    [JsonPropertyName("sourceSystem")]
    public string SourceSystem { get; init; } = string.Empty;
}

[XmlRoot("Envelope")]
public sealed class ExpectedJsonCustomerLookupSoapResponseEnvelope
{
    public ExpectedJsonCustomerLookupSoapResponseBody Body { get; set; } = new ExpectedJsonCustomerLookupSoapResponseBody();
}

public sealed class ExpectedJsonCustomerLookupSoapResponseBody
{
    public ExpectedJsonCustomerLookupSoapResponse CustomerLookupResponse { get; set; } = new ExpectedJsonCustomerLookupSoapResponse();
}

public sealed class ExpectedJsonCustomerLookupSoapResponse
{
    public string StatusCode { get; set; } = string.Empty;

    public string CustomerName { get; set; } = string.Empty;

    public string TraceId { get; set; } = string.Empty;
}



