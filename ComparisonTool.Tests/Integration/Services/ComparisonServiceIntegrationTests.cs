using System.Text;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using ComparisonTool.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ComparisonTool.Tests.Integration.Services;

public class ComparisonServiceIntegrationTests
{
    private readonly Mock<ILogger<ComparisonService>> _mockLogger;
    private readonly Mock<ILogger<ComparisonConfigurationService>> _mockConfigLogger;
    private readonly Mock<ILogger<XmlDeserializationService>> _mockXmlLogger;
    private readonly Mock<ILogger<FileSystemService>> _mockFileLogger;
    private readonly Mock<ILogger<PerformanceTracker>> _mockPerfLogger;
    private readonly Mock<ILogger<SystemResourceMonitor>> _mockResourceLogger;
    
    private readonly ComparisonConfigurationService _configService;
    private readonly XmlDeserializationService _xmlService;
    private readonly FileSystemService _fileService;
    private readonly PerformanceTracker _performanceTracker;
    private readonly SystemResourceMonitor _resourceMonitor;
    private readonly ComparisonResultCacheService _cacheService;
    private readonly ComparisonService _comparisonService;

    public ComparisonServiceIntegrationTests()
    {
        _mockLogger = new Mock<ILogger<ComparisonService>>();
        _mockConfigLogger = new Mock<ILogger<ComparisonConfigurationService>>();
        _mockXmlLogger = new Mock<ILogger<XmlDeserializationService>>();
        _mockFileLogger = new Mock<ILogger<FileSystemService>>();
        _mockPerfLogger = new Mock<ILogger<PerformanceTracker>>();
        _mockResourceLogger = new Mock<ILogger<SystemResourceMonitor>>();

        var configOptions = new ComparisonConfigurationOptions
        {
            MaxDifferences = 1000,
            DefaultIgnoreCollectionOrder = true,
            DefaultIgnoreStringCase = false
        };

        _configService = new ComparisonConfigurationService(_mockConfigLogger.Object, Options.Create(configOptions));
        
        var serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        _xmlService = new XmlDeserializationService(_mockXmlLogger.Object, serializerFactory);
        
        _fileService = new FileSystemService(_mockFileLogger.Object);
        _performanceTracker = new PerformanceTracker(_mockPerfLogger.Object);
        _resourceMonitor = new SystemResourceMonitor(_mockResourceLogger.Object);
        _cacheService = new ComparisonResultCacheService(_mockLogger.Object);

        var mockComparisonEngineLogger = new Mock<ILogger<ComparisonEngine>>();
        var comparisonEngine = new ComparisonEngine(mockComparisonEngineLogger.Object, _configService, _performanceTracker);
        
        var mockComparisonOrchestratorLogger = new Mock<ILogger<ComparisonOrchestrator>>();
        var comparisonOrchestrator = new ComparisonOrchestrator(
            mockComparisonOrchestratorLogger.Object,
            _xmlService,
            _configService,
            _fileService,
            _performanceTracker,
            _resourceMonitor,
            _cacheService,
            comparisonEngine);
        
        _comparisonService = new ComparisonService(
            _mockLogger.Object,
            _xmlService,
            _configService,
            _fileService,
            _performanceTracker,
            _resourceMonitor,
            _cacheService,
            comparisonEngine,
            comparisonOrchestrator);

        // Register test models
        _xmlService.RegisterDomainModel<TestModel>("TestModel");
        _xmlService.RegisterDomainModel<ComplexTestModel>("ComplexTestModel");
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithIdenticalFiles_ShouldReturnNoDifferences()
    {
        // Arrange
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act
        var result = await _comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithDifferentFiles_ShouldReturnDifferences()
    {
        // Arrange
        var xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Old Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        var xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>New Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml1));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml2));

        // Act
        var result = await _comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().HaveCount(1);
        result.Differences.First().PropertyName.Should().Be("StringProperty");
        result.Differences.First().Object1Value.Should().Be("Old Value");
        result.Differences.First().Object2Value.Should().Be("New Value");
        result.AreEqual.Should().BeFalse();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithIgnoredProperty_ShouldFilterDifferences()
    {
        // Arrange
        var xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Old Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        var xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>New Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml1));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml2));

        // Configure ignore rule
        _configService.IgnoreProperty("StringProperty");

        // Act
        var result = await _comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithComplexModel_ShouldHandleComplexStructures()
    {
        // Arrange
        var xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Test Model</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Item 1</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        var xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Test Model</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Item 1 Modified</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml1));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml2));

        // Act
        var result = await _comparisonService.CompareXmlFilesAsync(stream1, stream2, "ComplexTestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().NotBeEmpty();
        result.Differences.Should().Contain(d => d.PropertyName.Contains("Value"));
        result.AreEqual.Should().BeFalse();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithCollectionOrderIgnored_ShouldIgnoreOrder()
    {
        // Arrange
        var xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Test Model</Name>
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

        var xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Test Model</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>2</Id>
            <Value>Item 2</Value>
        </ComplexTestModelItem>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Item 1</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml1));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml2));

        // Configure to ignore collection order
        _configService.SetIgnoreCollectionOrder(true);

        // Act
        var result = await _comparisonService.CompareXmlFilesAsync(stream1, stream2, "ComplexTestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithUnregisteredModel_ShouldThrowException()
    {
        // Arrange
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act & Assert
        var action = () => _comparisonService.CompareXmlFilesAsync(stream1, stream2, "UnregisteredModel");
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task CompareXmlFilesAsync_WithMalformedXml_ShouldThrowException()
    {
        // Arrange
        var malformedXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>"; // Missing closing tag

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(malformedXml));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(malformedXml));

        // Act & Assert
        var action = () => _comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");
        await action.Should().ThrowAsync<System.Reflection.TargetInvocationException>(); // Exception is wrapped when called through reflection
    }

    [Fact]
    public async Task CompareXmlFilesWithCachingAsync_WithSameFiles_ShouldUseCache()
    {
        // Arrange
        var xml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Test Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml));

        // Act - First comparison
        var result1 = await _comparisonService.CompareXmlFilesWithCachingAsync(
            stream1, stream2, "TestModel", "file1.xml", "file2.xml");

        // Reset streams for second comparison
        stream1.Position = 0;
        stream2.Position = 0;

        // Act - Second comparison (should use cache)
        var result2 = await _comparisonService.CompareXmlFilesWithCachingAsync(
            stream1, stream2, "TestModel", "file1.xml", "file2.xml");

        // Assert
        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result1.AreEqual.Should().BeTrue();
        result2.AreEqual.Should().BeTrue();
        // Both results should be identical
        result1.Differences.Should().BeEquivalentTo(result2.Differences);
    }

    // Test helper classes
    [System.Xml.Serialization.XmlRoot("TestModel")]
    public class TestModel
    {
        [System.Xml.Serialization.XmlElement("StringProperty")]
        public string? StringProperty { get; set; }

        [System.Xml.Serialization.XmlElement("IntProperty")]
        public int IntProperty { get; set; }
    }

    [System.Xml.Serialization.XmlRoot("ComplexTestModel")]
    public class ComplexTestModel
    {
        [System.Xml.Serialization.XmlElement("Name")]
        public string? Name { get; set; }

        [System.Xml.Serialization.XmlArray("Items")]
        [System.Xml.Serialization.XmlArrayItem("ComplexTestModelItem")]
        public List<ComplexTestModelItem> Items { get; set; } = new();
    }

    public class ComplexTestModelItem
    {
        [System.Xml.Serialization.XmlElement("Id")]
        public int Id { get; set; }

        [System.Xml.Serialization.XmlElement("Value")]
        public string? Value { get; set; }
    }
} 