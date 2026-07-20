using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class ConsumerReportFixtureResponseModelTests
{
    [TestMethod]
    public void Register_WhenFixtureModelsAreRegistered_IncludesConsumerReportModels()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();

        ConsumerReportFixtureResponseModelRegistration.Register(registry);

        CollectionAssert.AreEquivalent(
            new[]
            {
                ConsumerReportFixtureResponseModelRegistration.SoapModelName,
                ConsumerReportFixtureResponseModelRegistration.JsonModelName,
            },
            registry.ListModelNames().ToArray());
    }

    [TestMethod]
    public async Task Deserialize_WhenConsumerSoapFixtureResponse_ReturnsRegisteredModel()
    {
        ResponseModelRegistry registry = CreateRegistry();
        JsonXmlResponseBodyDeserializer deserializer = new JsonXmlResponseBodyDeserializer(registry);
        string xml = "<soap:Envelope xmlns:soap=\"http://schemas.xmlsoap.org/soap/envelope/\" xmlns:cr=\"urn:paritybench:consumer-report\"><soap:Body><cr:ConsumerReportResponse><cr:ReportRequestId>CR-1001</cr:ReportRequestId><cr:ReportId>PB-A-CR-1001</cr:ReportId><cr:Score><cr:Value>742</cr:Value><cr:Band>Low</cr:Band><cr:ProbabilityOfDefault>0.014</cr:ProbabilityOfDefault></cr:Score><cr:RiskBand>Low</cr:RiskBand></cr:ConsumerReportResponse></soap:Body></soap:Envelope>";
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        object result = await deserializer.DeserializeAsync(
            ConsumerReportFixtureResponseModelRegistration.SoapModelName,
            stream,
            "application/xml",
            new ComparisonOptions(ignoreXmlNamespaces: true));

        ConsumerReportSoapResponseEnvelope response = (ConsumerReportSoapResponseEnvelope)result;
        Assert.AreEqual("CR-1001", response.Body.ConsumerReportResponse.ReportRequestId);
        Assert.AreEqual(742, response.Body.ConsumerReportResponse.Score.Value);
    }

    [TestMethod]
    public async Task Deserialize_WhenConsumerJsonFixtureResponse_ReturnsRegisteredModel()
    {
        ResponseModelRegistry registry = CreateRegistry();
        JsonXmlResponseBodyDeserializer deserializer = new JsonXmlResponseBodyDeserializer(registry);
        string json = "{\"reportRequestId\":\"CR-1001\",\"reportId\":\"PB-A-CR-1001\",\"score\":{\"value\":742,\"band\":\"Low\",\"probabilityOfDefault\":0.014},\"riskBand\":\"Low\"}";
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        object result = await deserializer.DeserializeAsync(
            ConsumerReportFixtureResponseModelRegistration.JsonModelName,
            stream,
            "application/json",
            new ComparisonOptions());

        ConsumerReportJsonResponse response = (ConsumerReportJsonResponse)result;
        Assert.AreEqual("CR-1001", response.ReportRequestId);
        Assert.AreEqual(742, response.Score.Value);
    }

    private static ResponseModelRegistry CreateRegistry()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();
        ConsumerReportFixtureResponseModelRegistration.Register(registry);
        return registry;
    }
}
