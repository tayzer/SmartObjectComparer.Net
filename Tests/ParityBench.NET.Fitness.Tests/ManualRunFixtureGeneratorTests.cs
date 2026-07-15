using System.Xml.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.ClientCustomerLookupExample;
using ParityBench.NET.ManualRunFixtureGenerator;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
public sealed class ManualRunFixtureGeneratorTests
{
    [TestMethod]
    public void Generate_WhenRequestingOneThousandFixtures_ProducesExpectedCountAndDistribution()
    {
        IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> fixtures = ClientCustomerLookupFixtureGenerator.Generate(1000, 10000);

        Assert.AreEqual(1000, fixtures.Count);

        IReadOnlyDictionary<ClientCustomerLookupVariation, int> distribution = ClientCustomerLookupFixtureGenerator.Summarize(fixtures);
        Assert.AreEqual(9, distribution.Count);
        Assert.AreEqual(1000, distribution.Values.Sum());
        foreach (int categoryCount in distribution.Values)
        {
            Assert.IsTrue(categoryCount is >= 111 and <= 112, $"Unexpected category count: {categoryCount}");
        }
    }

    [TestMethod]
    public void Generate_WhenCalledTwiceWithSameArguments_ProducesIdenticalOutput()
    {
        IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> first = ClientCustomerLookupFixtureGenerator.Generate(250, 10000);
        IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> second = ClientCustomerLookupFixtureGenerator.Generate(250, 10000);

        Assert.AreEqual(first.Count, second.Count);
        for (int i = 0; i < first.Count; i++)
        {
            Assert.AreEqual(first[i].FileName, second[i].FileName);
            Assert.AreEqual(first[i].Content, second[i].Content);
            Assert.AreEqual(first[i].Category, second[i].Category);
        }
    }

    [TestMethod]
    public void Generate_WhenProducingFixtures_EachFileIsWellFormedXml()
    {
        IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> fixtures = ClientCustomerLookupFixtureGenerator.Generate(20, 10000);
        XmlSerializer serializer = new XmlSerializer(typeof(ClientCustomerLookupSoapRequestEnvelope));

        foreach (ClientCustomerLookupFixtureGenerator.GeneratedFixture fixture in fixtures)
        {
            using StringReader reader = new StringReader(fixture.Content);
            ClientCustomerLookupSoapRequestEnvelope? envelope = (ClientCustomerLookupSoapRequestEnvelope?)serializer.Deserialize(reader);
            Assert.IsNotNull(envelope);
            Assert.IsFalse(string.IsNullOrWhiteSpace(envelope!.Body.LookupRequest.CustomerId));
            Assert.IsFalse(string.IsNullOrWhiteSpace(envelope.Body.LookupRequest.CorrelationId));
        }
    }
}
