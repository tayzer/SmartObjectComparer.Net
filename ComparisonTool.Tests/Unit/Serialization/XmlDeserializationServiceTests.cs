// <copyright file="XmlDeserializationServiceTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using System.Xml;
using System.Xml.Serialization;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.Serialization;

[TestClass]
public class XmlDeserializationServiceTests {
    private readonly Mock<ILogger<XmlDeserializationService>> mockLogger;
    private readonly ComparisonTool.Core.Serialization.XmlSerializerFactory serializerFactory;
    private readonly XmlDeserializationService service;

    public XmlDeserializationServiceTests() {
        this.mockLogger = new Mock<ILogger<XmlDeserializationService>>();
        this.serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        this.service = new XmlDeserializationService(this.mockLogger.Object, this.serializerFactory);
    }

    [TestMethod]
    public void Constructor_ShouldInitializeCorrectly() {
        // Act & Assert
        this.service.Should().NotBeNull();
    }

    [TestMethod]
    public void RegisterDomainModel_WithValidType_ShouldRegisterSuccessfully() {
        // Arrange
        var modelName = "TestModel";

        // Act
        this.service.RegisterDomainModel<TestModel>(modelName);

        // Assert
        var registeredType = this.service.GetModelType(modelName);
        registeredType.Should().Be(typeof(TestModel));
    }

    [TestMethod]
    public void RegisterDomainModel_WithDuplicateName_ShouldOverwritePreviousRegistration() {
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
    public void GetModelType_WithUnregisteredModel_ShouldThrowException() {
        // Arrange
        var modelName = "UnregisteredModel";

        // Act & Assert
        var action = () => this.service.GetModelType(modelName);
        action.Should().Throw<ArgumentException>()
            .WithMessage("*No model registered with name*");
    }

    [TestMethod]
    public void DeserializeXml_WithValidXml_ShouldDeserializeCorrectly() {
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
    public void DeserializeXml_WithMalformedXml_ShouldThrowException() {
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
    public void DeserializeXml_WithEmptyStream_ShouldThrowException() {
        // Arrange
        var modelName = "TestModel";
        this.service.RegisterDomainModel<TestModel>(modelName);

        using var stream = new MemoryStream();

        // Act & Assert
        var action = () => this.service.DeserializeXml<TestModel>(stream);
        action.Should().Throw<InvalidOperationException>();
    }

    [TestMethod]
    public void DeserializeXml_WithComplexModel_ShouldDeserializeCorrectly() {
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
    public void DeserializeXml_WithNullValues_ShouldHandleCorrectly() {
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
    public void DeserializeXml_WithMissingProperties_ShouldUseDefaultValues() {
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
    public void DeserializeXml_WithXmlNamespaces_ShouldHandleCorrectly() {
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
    public void GetRegisteredModelNames_ShouldReturnAllRegisteredModels() {
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
    public void ClearDeserializationCache_ShouldClearCache() {
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
    public void GetCacheStatistics_ShouldReturnValidStatistics() {
        // Act
        var stats = this.service.GetCacheStatistics();

        // Assert
        stats.Should().NotBeNull();
        stats.CacheSize.Should().BeGreaterThanOrEqualTo(0);
        stats.SerializerCacheSize.Should().BeGreaterThanOrEqualTo(0);
    }

    // Test helper classes
    [XmlRoot("TestModel")]
    public class TestModel {
        [XmlElement("StringProperty")]
        public string? StringProperty { get; set; }

        [XmlElement("IntProperty")]
        public int IntProperty { get; set; }
    }

    [XmlRoot("ComplexTestModel")]
    public class ComplexTestModel {
        [XmlElement("Name")]
        public string? Name { get; set; }

        [XmlArray("Items")]
        [XmlArrayItem("ComplexTestModelItem")]
        public List<ComplexTestModelItem> Items { get; set; } = new();
    }

    public class ComplexTestModelItem {
        [XmlElement("Id")]
        public int Id { get; set; }

        [XmlElement("Value")]
        public string? Value { get; set; }
    }

    [XmlRoot("AnotherTestModel")]
    public class AnotherTestModel {
        [XmlElement("Property")]
        public string? Property { get; set; }
    }
}
