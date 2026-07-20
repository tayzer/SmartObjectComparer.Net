using System.Text;
using System.Xml.Serialization;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Infrastructure;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class ResponseModelRegistryAndDeserializerTests
{
    [TestMethod]
    public void Resolve_WhenModelIsRegistered_ReturnsRegisteredType()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<JsonSampleResponse>("Sample");

        Type resolvedType = registry.Resolve("Sample");

        Assert.AreEqual(typeof(JsonSampleResponse), resolvedType);
        CollectionAssert.AreEqual(new[] { "Sample" }, registry.ListModelNames().ToArray());
    }

    [TestMethod]
    public void Resolve_WhenModelIsUnknown_ThrowsInvalidOperationException()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();

        AssertThrows<InvalidOperationException>(() => registry.Resolve("Missing"));
    }

    [TestMethod]
    public void Register_WhenModelNameAlreadyExists_ThrowsInvalidOperationException()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<JsonSampleResponse>("Sample");

        AssertThrows<InvalidOperationException>(() => registry.Register<JsonSampleResponse>("Sample"));
    }

    [TestMethod]
    public async Task DeserializeAsync_WhenBodyIsJson_CreatesExpectedModel()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<JsonSampleResponse>("Sample");
        JsonXmlResponseBodyDeserializer deserializer = new JsonXmlResponseBodyDeserializer(registry);
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes("{\"id\":7,\"name\":\"Alpha\"}"));

        object result = await deserializer.DeserializeAsync(
            "Sample",
            stream,
            "application/json",
            new ComparisonOptions());

        JsonSampleResponse response = (JsonSampleResponse)result;
        Assert.AreEqual(7, response.Id);
        Assert.AreEqual("Alpha", response.Name);
    }

    [TestMethod]
    public async Task DeserializeAsync_WhenXmlNamespacesAreIgnored_CreatesExpectedModel()
    {
        ResponseModelRegistry registry = new ResponseModelRegistry();
        registry.Register<XmlSampleResponse>("SampleXml");
        JsonXmlResponseBodyDeserializer deserializer = new JsonXmlResponseBodyDeserializer(registry);
        const string xml = "<ns:SampleResponse xmlns:ns=\"urn:test\"><ns:Id>7</ns:Id><ns:Name>Alpha</ns:Name></ns:SampleResponse>";
        using MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        object result = await deserializer.DeserializeAsync(
            "SampleXml",
            stream,
            "application/xml",
            new ComparisonOptions(ignoreXmlNamespaces: true));

        XmlSampleResponse response = (XmlSampleResponse)result;
        Assert.AreEqual(7, response.Id);
        Assert.AreEqual("Alpha", response.Name);
    }

    private static void AssertThrows<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }

    public sealed class JsonSampleResponse
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }

    [XmlRoot("SampleResponse")]
    public sealed class XmlSampleResponse
    {
        public int Id { get; set; }

        public string? Name { get; set; }
    }
}
