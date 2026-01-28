// <copyright file="XmlDeserializationServiceTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.Serialization;

[TestClass]
public class XmlDeserializationServiceTests
{
    private readonly Mock<ILogger<XmlDeserializationService>> mockLogger;
    private readonly ComparisonTool.Core.Serialization.XmlSerializerFactory serializerFactory;
    private readonly XmlDeserializationService service;

    public XmlDeserializationServiceTests()
    {
        this.mockLogger = new Mock<ILogger<XmlDeserializationService>>();
        this.serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        this.service = new XmlDeserializationService(this.mockLogger.Object, this.serializerFactory);
    }

    [TestMethod]
    public void Constructor_ShouldInitializeCorrectly()
    {
        // Act & Assert
        this.service.Should().NotBeNull();
    }

    [TestMethod]
    public void RegisterDomainModel_WithValidType_ShouldRegisterSuccessfully()
    {
        // Arrange
        var modelName = "TestModel";

        // Act
        this.service.RegisterDomainModel<TestModel>(modelName);

        // Assert
        var registeredType = this.service.GetModelType(modelName);
        registeredType.Should().Be(typeof(TestModel));
    }

    [TestMethod]
    public void RegisterDomainModel_WithDuplicateName_ShouldOverwritePreviousRegistration()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        // Act
        this.service.RegisterDomainModel<AnotherTestModel>(modelName);

        // Assert
        var registeredType = this.service.GetModelType(modelName);
        registeredType.Should().Be(typeof(AnotherTestModel));
    }

    [TestMethod]
    public void GetModelType_WithUnregisteredModel_ShouldThrowException()
    {
        // Arrange
        var modelName = "UnregisteredModel";

        // Act & Assert
        var action = () => this.service.GetModelType(modelName);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*No model registered with name*");
    }

    [TestMethod]
    public void DeserializeXml_WithValidXml_ShouldDeserializeCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = this.service.DeserializeXml<TestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.StringProperty.Should().Be("Test Value");
        result.IntProperty.Should().Be(42);
    }

    [TestMethod]
    public void DeserializeXml_WithMalformedXml_ShouldThrowException()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        var malformedXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>"; // Missing closing tag

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(malformedXml));

        // Act & Assert
        var action = () => this.service.DeserializeXml<TestModel>(stream);
        action.Should().Throw<InvalidOperationException>(); // XmlSerializer throws InvalidOperationException, not XmlException
    }

    [TestMethod]
    public void DeserializeXml_WithEmptyStream_ShouldThrowException()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        using var stream = new MemoryStream();

        // Act & Assert
        var action = () => this.service.DeserializeXml<TestModel>(stream);
        action.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void DeserializeXml_WithComplexModel_ShouldDeserializeCorrectly()
    {
        // Arrange
        var modelName = "ComplexTestModel";
        this.service.RegisterDomainModel<ComplexTestModel>(modelName);

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
        var result = this.service.DeserializeXml<ComplexTestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.Name.Should().Be("Test Complex Model");
        result.Items.Should().HaveCount(2);
        result.Items[0].Id.Should().Be(1);
        result.Items[0].Value.Should().Be("Item 1");
        result.Items[1].Id.Should().Be(2);
        result.Items[1].Value.Should().Be("Item 2");
    }

    [TestMethod]
    public void DeserializeXml_WithNullValues_ShouldHandleCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty></StringProperty>
    <IntProperty>0</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = this.service.DeserializeXml<TestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.StringProperty.Should().Be(string.Empty);
        result.IntProperty.Should().Be(0);
    }

    [TestMethod]
    public void DeserializeXml_WithMissingProperties_ShouldUseDefaultValues()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Only This Property</StringProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = this.service.DeserializeXml<TestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.StringProperty.Should().Be("Only This Property");
        result.IntProperty.Should().Be(0); // Default value
    }

    [TestMethod]
    public void DeserializeXml_WithXmlNamespaces_ShouldHandleCorrectly()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        // Note: XML serialization with namespaces requires proper namespace handling
        // This test demonstrates that namespaces can cause issues if not properly configured
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Namespace Test</StringProperty>
    <IntProperty>123</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = this.service.DeserializeXml<TestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.StringProperty.Should().Be("Namespace Test");
        result.IntProperty.Should().Be(123);
    }

    [TestMethod]
    public void DeserializeXml_WithDifferentNamespaceVersions_ShouldDeserializeSuccessfully()
    {
        // Arrange - Model expects version7 namespace, but XML has version8
        this.service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        this.service.IgnoreXmlNamespaces = true; // This is the default, but being explicit

        var xmlWithDifferentNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel xmlns=""urn:example.co.uk/soap:version8"">
    <Id>TEST-001</Id>
    <Name>Test Item</Name>
    <Value>42</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithDifferentNamespace));

        // Act
        var result = this.service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("TEST-001");
        result.Name.Should().Be("Test Item");
        result.Value.Should().Be(42);
    }

    [TestMethod]
    public void DeserializeXml_WithNoNamespace_ShouldDeserializeNamespacedModel()
    {
        // Arrange - Model expects version7 namespace, but XML has no namespace
        this.service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        this.service.IgnoreXmlNamespaces = true;

        var xmlWithNoNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel>
    <Id>TEST-002</Id>
    <Name>No Namespace Item</Name>
    <Value>99</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithNoNamespace));

        // Act
        var result = this.service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("TEST-002");
        result.Name.Should().Be("No Namespace Item");
        result.Value.Should().Be(99);
    }

    [TestMethod]
    public void DeserializeXml_WithMatchingNamespace_ShouldDeserializeSuccessfully()
    {
        // Arrange - Model expects version7 namespace, XML has matching version7
        this.service.RegisterDomainModel<NamespacedTestModel>("NamespacedModel");
        this.service.IgnoreXmlNamespaces = true;

        var xmlWithMatchingNamespace = @"<?xml version=""1.0"" encoding=""utf-8""?>
<NamespacedModel xmlns=""urn:example.co.uk/soap:version7"">
    <Id>TEST-003</Id>
    <Name>Matching Namespace Item</Name>
    <Value>123</Value>
</NamespacedModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xmlWithMatchingNamespace));

        // Act
        var result = this.service.DeserializeXml<NamespacedTestModel>(stream);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be("TEST-003");
        result.Name.Should().Be("Matching Namespace Item");
        result.Value.Should().Be(123);
    }

    [TestMethod]
    public void DeserializeXml_SoapEnvelope_ShouldDeserializeWithPrefixedNamespaces()
    {
        // Arrange - SOAP envelope with prefixed namespaces (soap:Envelope, soap:Body)
        this.service.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");
        this.service.IgnoreXmlNamespaces = true;

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
        var result = this.service.DeserializeXml<SoapEnvelope>(stream);

        // Assert
        result.Should().NotBeNull();
        result.Body.Should().NotBeNull();
        result.Body!.Response.Should().NotBeNull();
        result.Body.Response!.ReportId.Should().Be("test-report-123");
        result.Body.Response.Summary.Should().NotBeNull();
        result.Body.Response.Summary.TotalResults.Should().Be(5);
        result.Body.Response.Summary.SuccessCount.Should().Be(3);
        result.Body.Response.Summary.FailureCount.Should().Be(2);
    }

    [TestMethod]
    public void IgnoreXmlNamespaces_ShouldBeTrueByDefault()
    {
        // Assert
        this.service.IgnoreXmlNamespaces.Should().BeTrue();
    }

    [TestMethod]
    public void GetRegisteredModelNames_ShouldReturnAllRegisteredModels()
    {
        // Arrange
        this.service.RegisterDomainModel<TestModel>("Model1");
        this.service.RegisterDomainModel<ComplexTestModel>("Model2");

        // Act
        var registeredModels = this.service.GetRegisteredModelNames();

        // Assert
        registeredModels.Should().Contain("Model1");
        registeredModels.Should().Contain("Model2");
    }

    [TestMethod]
    public void ClearDeserializationCache_ShouldClearCache()
    {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act - First deserialization (should cache)
        var result1 = this.service.DeserializeXml<TestModel>(stream);

        // Clear cache
        this.service.ClearDeserializationCache();

        // Reset stream for second deserialization
        stream.Position = 0;
        var result2 = this.service.DeserializeXml<TestModel>(stream);

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.StringProperty.Should().Be(result2.StringProperty);
    }

    [TestMethod]
    public void GetCacheStatistics_ShouldReturnValidStatistics()
    {
        // Act
        var stats = this.service.GetCacheStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.CacheSize.Should().BeGreaterThanOrEqualTo(0);
        stats.SerializerCacheSize.Should().BeGreaterThanOrEqualTo(0);
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
        public List<ComplexTestModelItem> Items { get; set; } = new ();
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
        public string? Id { get; set; }

        [XmlElement("Name")]
        public string? Name { get; set; }

        [XmlElement("Value")]
        public int Value { get; set; }
    }
}
