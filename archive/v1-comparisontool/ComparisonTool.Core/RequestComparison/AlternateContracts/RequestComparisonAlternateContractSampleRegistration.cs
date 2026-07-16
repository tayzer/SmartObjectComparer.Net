using ComparisonTool.Core.DI;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;

namespace ComparisonTool.Core.RequestComparison.AlternateContracts;

/// <summary>
/// Registers a repo-local sample alternate-contract profile for host discoverability and integration testing.
/// </summary>
public static class RequestComparisonAlternateContractSampleRegistration
{
    public const string SampleModelName = "SampleSoapCustomerLookupResponseEnvelope";
    public const string SampleProfileId = "sample-soap-to-json";

    /// <summary>
    /// Registers the canonical comparison model used by the sample alternate-contract profile.
    /// </summary>
    public static void RegisterComparisonModels(XmlComparisonOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RegisterDomainModelWithRootElement<SampleSoapCustomerLookupResponseEnvelope>(SampleModelName, "Envelope");
    }

    /// <summary>
    /// Registers the sample SOAP-to-JSON alternate-contract profile.
    /// </summary>
    public static void RegisterProfiles(RequestComparisonAlternateContractOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RegisterAlternateContract<
            SampleSoapCustomerLookupRequestEnvelope,
            SampleAlternateJsonCustomerLookupRequest,
            SampleSoapCustomerLookupResponseEnvelope,
            SampleAlternateJsonCustomerLookupResponse,
            SampleSoapToJsonMapper>(
            canonicalModelName: SampleModelName,
            profileId: SampleProfileId,
            configure: builder => builder
                .SupportSourceRequestFormats(SerializationFormat.Xml)
                .UseAlternateRequestFormat(SerializationFormat.Json, "application/json")
                .UseAlternateResponseFormat(SerializationFormat.Json)
                .MapCanonicalResponsePropertyPath(
                    "Envelope.Body.CustomerLookupResponse.SensitiveToken",
                    "payload.raw_token"));
    }

    private sealed class SampleSoapToJsonMapper
        : IAlternateContractMapper<
            SampleSoapCustomerLookupRequestEnvelope,
            SampleAlternateJsonCustomerLookupRequest,
            SampleSoapCustomerLookupResponseEnvelope,
            SampleAlternateJsonCustomerLookupResponse>
    {
        public SampleAlternateJsonCustomerLookupRequest MapRequest(SampleSoapCustomerLookupRequestEnvelope canonicalRequest)
            => new()
            {
                LookupId = canonicalRequest.Body.CustomerLookupRequest.CustomerId,
                RawToken = canonicalRequest.Body.CustomerLookupRequest.SensitiveToken,
            };

        public SampleSoapCustomerLookupResponseEnvelope MapResponse(SampleAlternateJsonCustomerLookupResponse alternateResponse)
            => new()
            {
                Body = new SampleSoapCustomerLookupResponseBody
                {
                    CustomerLookupResponse = new SampleSoapCustomerLookupResponse
                    {
                        StatusCode = alternateResponse.StatusCode,
                        CustomerName = alternateResponse.CustomerName,
                        SensitiveToken = alternateResponse.Payload.RawToken,
                    },
                },
            };
    }
}