using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.TestEndpoints;
using ParityBench.NET.TestEndpoints.ConsumerReports;

namespace ParityBench.NET.TestEndpoints.Tests;

[TestClass]
public sealed class ConsumerReportFixtureTests
{
    [TestMethod]
    public void ConsumerReportSoap_WhenScenarioIsVolatileOnly_ReturnsBusinessEqualMetadataDifferences()
    {
        ConsumerReportRequest request = CreateRequest("CR-1001");

        ConsumerReportResponse responseA = ConsumerReportFixtures.CreateResponse(EndpointVariant.A, request);
        ConsumerReportResponse responseB = ConsumerReportFixtures.CreateResponse(EndpointVariant.B, request);

        Assert.AreEqual(responseA.Score.Value, responseB.Score.Value);
        Assert.AreEqual(responseA.RiskBand, responseB.RiskBand);
        Assert.AreNotEqual(responseA.ReportId, responseB.ReportId);
        Assert.AreNotEqual(responseA.ProviderTraceId, responseB.ProviderTraceId);
    }

    [TestMethod]
    public void ConsumerReportJson_WhenScenarioUsesMaskableIdentifiers_ReturnsDifferentRawIdentifiersWithSameLastFour()
    {
        ConsumerReportRequest request = CreateRequest("CR-1004");

        ConsumerReportResponse responseA = ConsumerReportFixtures.CreateResponse(EndpointVariant.A, request);
        ConsumerReportResponse responseB = ConsumerReportFixtures.CreateResponse(EndpointVariant.B, request);

        Assert.AreNotEqual(responseA.Subject.NationalIdentifier, responseB.Subject.NationalIdentifier);
        Assert.AreEqual(
            responseA.Subject.NationalIdentifier[^4..],
            responseB.Subject.NationalIdentifier[^4..]);
    }

    [TestMethod]
    public void ConsumerReportJson_WhenScenarioUsesNestedContactPreferences_ReturnsTwoDifferencesOnNestedObject()
    {
        ConsumerReportRequest request = CreateRequest("CR-2002");

        ConsumerReportResponse responseA = ConsumerReportFixtures.CreateResponse(EndpointVariant.A, request);
        ConsumerReportResponse responseB = ConsumerReportFixtures.CreateResponse(EndpointVariant.B, request);

        Assert.AreEqual(
            responseA.Subject.ContactProfile.PrimaryChannel.EmailAddress,
            responseB.Subject.ContactProfile.PrimaryChannel.EmailAddress);
        Assert.AreNotEqual(
            responseA.Subject.ContactProfile.NotificationPreference.StatementDelivery,
            responseB.Subject.ContactProfile.NotificationPreference.StatementDelivery);
        Assert.AreNotEqual(
            responseA.Subject.ContactProfile.NotificationPreference.MarketingConsent,
            responseB.Subject.ContactProfile.NotificationPreference.MarketingConsent);
    }

    private static ConsumerReportRequest CreateRequest(string reportRequestId) =>
        new ConsumerReportRequest(
            reportRequestId,
            "Alex",
            "Morgan",
            $"CONSENT-{reportRequestId}",
            $"test-{reportRequestId}");
}
