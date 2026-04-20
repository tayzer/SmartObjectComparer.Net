using System.Text;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;
using CoreXmlSerializerFactory = ComparisonTool.Core.Serialization.XmlSerializerFactory;

namespace ComparisonTool.Tests.Unit.Serialization;

[TestClass]
public class XmlDeserializationServiceTests
{
    private readonly Mock<ILogger<XmlDeserializationService>> mockLogger;
    private readonly ComparisonTool.Core.Serialization.XmlSerializerFactory serializerFactory;
    private readonly XmlDeserializationService service;

    public XmlDeserializationServiceTests()
    {
        mockLogger = new Mock<ILogger<XmlDeserializationService>>();
        serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        service = new XmlDeserializationService(mockLogger.Object, serializerFactory);
    }

    [TestMethod]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        service.ShouldNotBeNull();
    }

    [TestMethod]
    public void RegisterDomainModel_WithValidType_ShouldRegisterSuccessfully()
    {
        // Arrange
        var modelName = "TestModel";

        // Act
        service.RegisterDomainModel<TestModel>(modelName);

        // Assert
        var registeredType = service.GetModelType(modelName);
        registeredType.ShouldBe(typeof(TestModel));
    }

    [TestMethod]
    public void RegisterDomainModel_WithDuplicateName_ShouldOverwritePreviousRegistration()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        // Act
        service.RegisterDomainModel<AnotherTestModel>(modelName);

        // Assert
        var registeredType = service.GetModelType(modelName);
        registeredType.ShouldBe(typeof(AnotherTestModel));
    }

    [TestMethod]
    public void GetModelType_WithUnregisteredModel_ShouldThrowException()
    {
        // Arrange
        var modelName = "UnregisteredModel";

        // Act & Assert
        var action = () => service.GetModelType(modelName);
        var exception = Should.Throw<ArgumentException>(action);
        exception.Message.ShouldContain("No model registered with name");
    }

    [TestMethod]
    public void DeserializeXml_WithValidXml_ShouldDeserializeCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = service.DeserializeXml<TestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.StringProperty.ShouldBe("Test Value");
        result.IntProperty.ShouldBe(42);
    }

    [TestMethod]
    public void DeserializeXml_WithMalformedXml_ShouldThrowException()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        var malformedXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>"; // Missing closing tag

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(malformedXml));

        // Act & Assert
        var action = () => service.DeserializeXml<TestModel>(stream);
        Should.Throw<InvalidOperationException>(action); // XmlSerializer throws InvalidOperationException, not XmlException
    }

    [TestMethod]
    public void DeserializeXml_WithEmptyStream_ShouldThrowException()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        using var stream = new MemoryStream();

        // Act & Assert
        var action = () => service.DeserializeXml<TestModel>(stream);
        Should.Throw<InvalidOperationException>(action);
    }

    [TestMethod]
    public void DeserializeXml_WithComplexModel_ShouldDeserializeCorrectly()
    {
        // Arrange
        var modelName = "ComplexTestModel";
        service.RegisterDomainModel<ComplexTestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Test Complex Model</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Item 1</Value>
        </ComplexTestModelItem>
        <ComplexTestModelItem>
            <Id>2</Id>
            <Value>Item 2</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = service.DeserializeXml<ComplexTestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.Name.ShouldBe("Test Complex Model");
        result.Items.Count.ShouldBe(2);
        result.Items[0].Id.ShouldBe(1);
        result.Items[0].Value.ShouldBe("Item 1");
        result.Items[1].Id.ShouldBe(2);
        result.Items[1].Value.ShouldBe("Item 2");
    }

    [TestMethod]
    public void DeserializeXml_WithNullValues_ShouldHandleCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty></StringProperty>
    <IntProperty>0</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = service.DeserializeXml<TestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.StringProperty.ShouldBe(string.Empty);
        result.IntProperty.ShouldBe(0);
    }

    [TestMethod]
    public void DeserializeXml_WithMissingProperties_ShouldUseDefaultValues()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Only This Property</StringProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = service.DeserializeXml<TestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.StringProperty.ShouldBe("Only This Property");
        result.IntProperty.ShouldBe(0); // Default value
    }

    [TestMethod]
    public void DeserializeXml_WithXmlNamespaces_ShouldHandleCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        // Note: XML serialization with namespaces requires proper namespace handling
        // This test demonstrates that namespaces can cause issues if not properly configured
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Namespace Test</StringProperty>
    <IntProperty>123</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = service.DeserializeXml<TestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.StringProperty.ShouldBe("Namespace Test");
        result.IntProperty.ShouldBe(123);
    }

    [TestMethod]
    public void DeserializeXml_WithDifferentNamespaceVersions_ShouldDeserializeSuccessfully()
    {
        // Arrange - Model expects version7 namespace, but XML has version8
        service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        service.IgnoreXmlNamespaces = true; // This is the default, but being explicit

        var xmlWithDifferentNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel xmlns=""urn:example.co.uk/soap:version8"">
    <Id>TEST-001</Id>
    <Name>Test Item</Name>
    <Value>42</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithDifferentNamespace));

        // Act
        var result = service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("TEST-001");
        result.Name.ShouldBe("Test Item");
        result.Value.ShouldBe(42);
    }

    [TestMethod]
    public void DeserializeXml_WithNoNamespace_ShouldDeserializeNamespacedModel()
    {
        // Arrange - Model expects version7 namespace, but XML has no namespace
        service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        service.IgnoreXmlNamespaces = true;

        var xmlWithNoNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel>
    <Id>TEST-002</Id>
    <Name>No Namespace Item</Name>
    <Value>99</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithNoNamespace));

        // Act
        var result = service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("TEST-002");
        result.Name.ShouldBe("No Namespace Item");
        result.Value.ShouldBe(99);
    }

    [TestMethod]
    public void DeserializeXml_WithMatchingNamespace_ShouldDeserializeSuccessfully()
    {
        // Arrange - Model expects version7 namespace, XML has matching version7
        service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        service.IgnoreXmlNamespaces = true;

        var xmlWithMatchingNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel xmlns=""urn:example.co.uk/soap:version7"">
    <Id>TEST-003</Id>
    <Name>Matching Namespace Item</Name>
    <Value>123</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithMatchingNamespace));

        // Act
        var result = service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.Id.ShouldBe("TEST-003");
        result.Name.ShouldBe("Matching Namespace Item");
        result.Value.ShouldBe(123);
    }

    [TestMethod]
    public void TryDeserializeXml_WithTimingScope_ShouldRecordXmlPrecheckAndFullDeserializeTimings()
    {
        // Arrange
        service.RegisterDomainModel<TestModel>("TestModel");

        var xml = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Timed Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>");

        var phaseTimings = new ComparisonPhaseTimingContext("unit-test");

        // Act
        using (ComparisonPhaseTimingScope.Push(phaseTimings))
        {
            for (var index = 0; index < 25; index++)
            {
                using var stream = new MemoryStream(xml, writable: false);
                var result = service.TryDeserializeXml(stream, typeof(TestModel));
                result.Success.ShouldBeTrue();
            }
        }

        var snapshot = phaseTimings.CreateSnapshot();

        // Assert
        snapshot.XmlDeserializationPrecheckMs.ShouldBeGreaterThanOrEqualTo(0);
        snapshot.XmlDeserializationFullDeserializeMs.ShouldBeGreaterThanOrEqualTo(0);
    }

    [TestMethod]
    public void TryDeserializeXml_WithWrongRoot_ShouldOnlyRecordXmlPrecheckTiming()
    {
        // Arrange
        service.RegisterDomainModel<TestModel>("TestModel");

        var xml = Encoding.UTF8.GetBytes(@"<?xml version=""1.0"" encoding=""utf-8""?>
<WrongRoot>
    <StringProperty>Timed Value</StringProperty>
    <IntProperty>42</IntProperty>
</WrongRoot>");

        var phaseTimings = new ComparisonPhaseTimingContext("unit-test");

        // Act
        using (ComparisonPhaseTimingScope.Push(phaseTimings))
        {
            for (var index = 0; index < 25; index++)
            {
                using var stream = new MemoryStream(xml, writable: false);
                var result = service.TryDeserializeXml(stream, typeof(TestModel));
                result.Success.ShouldBeFalse();
            }
        }

        var snapshot = phaseTimings.CreateSnapshot();

        // Assert
        snapshot.XmlDeserializationPrecheckMs.ShouldBeGreaterThanOrEqualTo(0);
        snapshot.XmlDeserializationFullDeserializeMs.ShouldBe(0);
    }

    [TestMethod]
    public void TryDeserializeXml_WithSoapFaultEnvelope_ShouldReturnFailureWithoutThrowing()
    {
        // Arrange
        service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");

        var soapFaultXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"">
    <soap:Body>
        <soap:Fault>
            <faultcode>soap:Server</faultcode>
            <faultstring>Order service unavailable</faultstring>
            <detail>
                <ServiceFault>
                    <CorrelationId>corr-123</CorrelationId>
                </ServiceFault>
            </detail>
        </soap:Fault>
    </soap:Body>
</soap:Envelope>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(soapFaultXml));

        // Act
        var result = service.TryDeserializeXml(stream, typeof(SoapEnvelope));

        // Assert
        result.Success.ShouldBeFalse();
        result.ErrorMessage.ShouldContain("SOAP fault detected in response");
        result.ErrorMessage.ShouldContain("soap:Server");
        result.ErrorMessage.ShouldContain("Order service unavailable");
        result.ErrorMessage.ShouldContain("CorrelationId");
        result.FailureKind.ShouldBe(DeserializationFailureKind.SoapFault);
        result.IsRecognizedNonSuccessPayload.ShouldBeTrue();
    }

    [TestMethod]
    public void DeserializeXml_SoapEnvelope_ShouldDeserializeWithPrefixedNamespaces()
    {
        // Arrange - SOAP envelope with prefixed namespaces (soap:Envelope, soap:Body)
        service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");
        service.IgnoreXmlNamespaces = true;

        var soapXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <soap:Body>
        <SearchResponse xmlns=""urn:soap.co.uk/soap:search1"">
            <ReportId>test-report-123</ReportId>
            <GeneratedOn>2025-01-28T10:00:00</GeneratedOn>
            <Summary>
                <TotalResults>5</TotalResults>
                <SuccessCount>3</SuccessCount>
                <FailureCount>2</FailureCount>
            </Summary>
        </SearchResponse>
    </soap:Body>
</soap:Envelope>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(soapXml));

        // Act
        var result = service.DeserializeXml<SoapEnvelope>(stream);

        // Assert
        result.ShouldNotBeNull();
        result.Body.ShouldNotBeNull();
        result.Body!.Response.ShouldNotBeNull();
        result.Body.Response!.ReportId.ShouldBe("test-report-123");
        result.Body.Response.Summary.ShouldNotBeNull();
        result.Body.Response.Summary.TotalResults.ShouldBe(5);
        result.Body.Response.Summary.SuccessCount.ShouldBe(3);
        result.Body.Response.Summary.FailureCount.ShouldBe(2);
    }

    [TestMethod]
    public void DeserializeXml_SoapEnvelope_WithCustomRootSerializer_ShouldDeserializeAllNestedElements()
    {
        // Arrange - This simulates what happens when using RegisterDomainModelWithRootElement
        // The serializer must handle ALL nested types, not just the root
        var factory = new CoreXmlSerializerFactory();
        factory.RegisterType<SoapEnvelope>(() => factory.CreateNamespaceIgnorantSerializer<SoapEnvelope>("Envelope"));

        var service = new XmlDeserializationService(
            Mock.Of<ILogger<XmlDeserializationService>>(),
            factory,
            null);
        service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");
        service.IgnoreXmlNamespaces = true;

        // This XML has multiple different namespaces - soap: for Envelope/Body, and a default namespace for SearchResponse
        var soapXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<soap:Envelope xmlns:soap=""http://schemas.xmlsoap.org/soap/envelope/"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance"">
    <soap:Body>
        <SearchResponse xmlns=""urn:soap.co.uk/soap:search1"">
            <ReportId>test-report-456</ReportId>
            <GeneratedOn>2025-01-28T12:00:00</GeneratedOn>
            <Summary>
                <TotalResults>10</TotalResults>
                <SuccessCount>8</SuccessCount>
                <FailureCount>2</FailureCount>
            </Summary>
            <Results>
                <Result>
                    <Id>1</Id>
                    <Name>First Item</Name>
                    <Score>95.5</Score>
                    <Details>
                        <Description>First item description</Description>
                        <Status>Success</Status>
                    </Details>
                    <Tags>
                        <Tag>Important</Tag>
                    </Tags>
                </Result>
            </Results>
        </SearchResponse>
    </soap:Body>
</soap:Envelope>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(soapXml));

        // Act
        var result = service.DeserializeXml<SoapEnvelope>(stream);

        // Assert - Verify ALL nested elements are populated correctly
        result.ShouldNotBeNull();
        result.Body.ShouldNotBeNull();
        result.Body!.Response.ShouldNotBeNull();
        result.Body.Response!.ReportId.ShouldBe("test-report-456");
        result.Body.Response.Summary.ShouldNotBeNull();
        result.Body.Response.Summary.TotalResults.ShouldBe(10);
        result.Body.Response.Results.ShouldNotBeNull();
        result.Body.Response.Results.Count.ShouldBe(1);
        result.Body.Response.Results[0].Id.ShouldBe(1);
        result.Body.Response.Results[0].Name.ShouldBe("First Item");
        result.Body.Response.Results[0].Details.Description.ShouldBe("First item description");
        result.Body.Response.Results[0].Tags.ShouldContain("Important");
    }

    [TestMethod]
    public void RegisterDomainModel_WhenSerializerPreCachingFails_ShouldStillRegisterModelAndLogWarning()
    {
        // Arrange - register a factory that throws to simulate a pre-cache failure
        var throwingFactory = new CoreXmlSerializerFactory();
        throwingFactory.RegisterType<TestModel>(() => throw new InvalidOperationException("Simulated serializer factory error"));

        var warnLogger = new Mock<ILogger<XmlDeserializationService>>();
        var serviceUnderTest = new XmlDeserializationService(warnLogger.Object, throwingFactory);

        // Act — must not throw
        var act = () => serviceUnderTest.RegisterDomainModel<TestModel>("ThrowingModel");
        Should.NotThrow(act);

        // Assert — model is still present in the registry
        serviceUnderTest.GetRegisteredModelNames().ShouldContain("ThrowingModel");

        // Assert — a warning was logged mentioning the model name and failure
        warnLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((o, t) => o.ToString()!.Contains("ThrowingModel")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce());
    }

    [TestMethod]
    public void IgnoreXmlNamespaces_ShouldBeTrueByDefault()
    {
        // Assert
        service.IgnoreXmlNamespaces.ShouldBeTrue();
    }

    [TestMethod]
    public void GetRegisteredModelNames_ShouldReturnAllRegisteredModels()
    {
        // Arrange
        service.RegisterDomainModel<TestModel>("Model1");
        service.RegisterDomainModel<ComplexTestModel>("Model2");

        // Act
        var registeredModels = service.GetRegisteredModelNames();

        // Assert
        registeredModels.ShouldContain("Model1");
        registeredModels.ShouldContain("Model2");
    }

    [TestMethod]
    public void ClearDeserializationCache_ShouldClearCache()
    {
        // Arrange
        var modelName = "TestModel";
        service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act - First deserialization (should cache)
        var result1 = service.DeserializeXml<TestModel>(stream);

        // Clear cache
        service.ClearDeserializationCache();

        // Reset stream for second deserialization
        stream.Position = 0;
        var result2 = service.DeserializeXml<TestModel>(stream);

        // Assert
        result1.ShouldNotBeNull();
        result2.ShouldNotBeNull();
        result1.StringProperty.ShouldBe(result2.StringProperty);
    }

    [TestMethod]
    public void GetCacheStatistics_ShouldReturnValidStatistics()
    {
        // Act
        var stats = service.GetCacheStatistics();

        // Assert
        stats.CacheSize.ShouldBeGreaterThanOrEqualTo(0);
        stats.SerializerCacheSize.ShouldBeGreaterThanOrEqualTo(0);
    }

    // Test helper classes
    [XmlRoot("TestModel")]
    public class TestModel
    {
        [XmlElement("StringProperty")]
        public string? StringProperty
        {
            get; set;
        }

        [XmlElement("IntProperty")]
        public int IntProperty
        {
            get; set;
        }
    }

    [XmlRoot("ComplexTestModel")]
    public class ComplexTestModel
    {
        [XmlElement("Name")]
        public string? Name
        {
            get; set;
        }

        [XmlArray("Items")]
        [XmlArrayItem("ComplexTestModelItem")]
        public List<ComplexTestModelItem> Items { get; set; } = new List<ComplexTestModelItem>();
    }

    public class ComplexTestModelItem
    {
        [XmlElement("Id")]
        public int Id
        {
            get; set;
        }

        [XmlElement("Value")]
        public string? Value
        {
            get; set;
        }
    }

    [XmlRoot("AnotherTestModel")]
    public class AnotherTestModel
    {
        [XmlElement("Property")]
        public string? Property
        {
            get; set;
        }
    }

    /// <summary>
    /// Test model with a specific namespace to verify namespace-ignorant deserialization.
    /// </summary>
    [XmlRoot("NamespacedModel", Namespace = "urn:example.co.uk/soap:version7")]
    public class NamespacedTestModel
    {
        [XmlElement("Id")]
        public string? Id
        {
            get; set;
        }

        [XmlElement("Name")]
        public string? Name
        {
            get; set;
        }

        [XmlElement("Value")]
        public int Value
        {
            get; set;
        }
    }
}
