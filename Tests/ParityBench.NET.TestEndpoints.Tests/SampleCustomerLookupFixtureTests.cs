using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.TestEndpoints;
using ParityBench.NET.TestEndpoints.SampleCustomerLookup;

namespace ParityBench.NET.TestEndpoints.Tests;

[TestClass]
public sealed class SampleCustomerLookupFixtureTests
{
    [TestMethod]
    public void SampleCustomerLookup_WhenJsonEndpointReceivesProfileRequest_ReturnsProfileCompatibleResponse()
    {
        SampleCustomerLookupJsonRequest request = new SampleCustomerLookupJsonRequest
        {
            LookupId = "1001",
            RawToken = "manual-token-1001",
        };

        SampleCustomerLookupJsonResponse response = SampleCustomerLookupFixtures.CreateJsonResponse(EndpointVariant.B, request);

        Assert.AreEqual("OK", response.StatusCode);
        Assert.AreEqual("Avery Stone", response.CustomerName);
        Assert.AreEqual("manual-token-1001", response.Payload.RawToken);
    }

    [TestMethod]
    public void SampleCustomerLookup_WhenScenarioHasTokenOnlyDifference_ReturnsDifferentTokensWithSameLastFour()
    {
        SampleCustomerLookupRequest soapRequest = new SampleCustomerLookupRequest
        {
            CustomerId = "1003",
            SensitiveToken = "manual-token-9999",
        };
        SampleCustomerLookupJsonRequest jsonRequest = new SampleCustomerLookupJsonRequest
        {
            LookupId = "1003",
            RawToken = "manual-token-9999",
        };

        SampleCustomerLookupSoapResponse responseA = SampleCustomerLookupFixtures.CreateSoapResponse(EndpointVariant.A, soapRequest);
        SampleCustomerLookupJsonResponse responseB = SampleCustomerLookupFixtures.CreateJsonResponse(EndpointVariant.B, jsonRequest);

        Assert.AreNotEqual(responseA.SensitiveToken, responseB.Payload.RawToken);
        Assert.AreEqual(responseA.SensitiveToken[^4..], responseB.Payload.RawToken[^4..]);
    }
}
