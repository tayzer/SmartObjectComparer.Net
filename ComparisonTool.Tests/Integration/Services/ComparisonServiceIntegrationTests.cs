using System.IO;
using System.Text;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using ComparisonTool.Domain.Models;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Integration.Services;

[TestClass]
public class ComparisonServiceIntegrationTests
{
    private readonly Mock<ILogger<ComparisonService>> mockLogger;
    private readonly Mock<ILogger<ComparisonConfigurationService>> mockConfigLogger;
    private readonly Mock<ILogger<XmlDeserializationService>> mockXmlLogger;
    private readonly Mock<ILogger<FileSystemService>> mockFileLogger;
    private readonly Mock<ILogger<PerformanceTracker>> mockPerfLogger;
    private readonly Mock<ILogger<SystemResourceMonitor>> mockResourceLogger;

    private readonly ComparisonConfigurationService configService;
    private readonly XmlDeserializationService xmlService;
    private readonly FileSystemService fileService;
    private readonly PerformanceTracker performanceTracker;
    private readonly SystemResourceMonitor resourceMonitor;
    private readonly ComparisonResultCacheService cacheService;
    private readonly ComparisonService comparisonService;

    public ComparisonServiceIntegrationTests()
    {
        mockLogger = new Mock<ILogger<ComparisonService>>();
        mockConfigLogger = new Mock<ILogger<ComparisonConfigurationService>>();
        mockXmlLogger = new Mock<ILogger<XmlDeserializationService>>();
        mockFileLogger = new Mock<ILogger<FileSystemService>>();
        mockPerfLogger = new Mock<ILogger<PerformanceTracker>>();
        mockResourceLogger = new Mock<ILogger<SystemResourceMonitor>>();

        var configOptions = new ComparisonConfigurationOptions
        {
            MaxDifferences = 1000,
            DefaultIgnoreCollectionOrder = true,
            DefaultIgnoreStringCase = false,
        };

        configService = new ComparisonConfigurationService(mockConfigLogger.Object, Options.Create(configOptions));

        var serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        serializerFactory.RegisterType<ComplexOrderResponse>(
            () => serializerFactory.CreateComplexOrderResponseSerializer());
        xmlService = new XmlDeserializationService(mockXmlLogger.Object, serializerFactory);

        fileService = new FileSystemService(mockFileLogger.Object);
        performanceTracker = new PerformanceTracker(mockPerfLogger.Object);
        resourceMonitor = new SystemResourceMonitor(mockResourceLogger.Object);
        cacheService = new ComparisonResultCacheService(mockLogger.Object);

        var mockComparisonEngineLogger = new Mock<ILogger<ComparisonEngine>>();
        var comparisonEngine = new ComparisonEngine(mockComparisonEngineLogger.Object, configService, performanceTracker);

        var mockComparisonOrchestratorLogger = new Mock<ILogger<ComparisonOrchestrator>>();
        var comparisonOrchestrator = new ComparisonOrchestrator(
            mockComparisonOrchestratorLogger.Object,
            xmlService,
            configService,
            fileService,
            performanceTracker,
            resourceMonitor,
            cacheService,
            comparisonEngine);

        comparisonService = new ComparisonService(
            mockLogger.Object,
            xmlService,
            configService,
            fileService,
            performanceTracker,
            resourceMonitor,
            cacheService,
            comparisonEngine,
            comparisonOrchestrator);

        // Register test models
        xmlService.RegisterDomainModel<TestModel>("TestModel");
        xmlService.RegisterDomainModel<ComplexTestModel>("ComplexTestModel");
        xmlService.RegisterDomainModel<ComplexOrderResponse>("ComplexOrderResponse");
    }

    [TestMethod]
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
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [TestMethod]
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
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().HaveCount(1);
        result.Differences.First().PropertyName.Should().Be("StringProperty");
        result.Differences.First().Object1Value.Should().Be("Old Value");
        result.Differences.First().Object2Value.Should().Be("New Value");
        result.AreEqual.Should().BeFalse();
    }

    [TestMethod]
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
        configService.IgnoreProperty("StringProperty");

        // Act
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [TestMethod]
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
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "ComplexTestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().NotBeEmpty();
        result.Differences.Should().Contain(d => d.PropertyName.Contains("Value"));
        result.AreEqual.Should().BeFalse();
    }

    [TestMethod]
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
        configService.SetIgnoreCollectionOrder(true);

        // Act
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "ComplexTestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [DataTestMethod]
    [DataRow("Actual_4_Differences.xml", "Expected_4_Differences.xml", false)]
    [DataRow("Actual_Component_Timings_Order.xml", "Expected_Component_Timings_Order.xml", true)]
    [DataRow("Actual_DateTime_Diff.xml", "Expected_DateTime_Diff.xml", false)]
    [DataRow("Actual_SourceSystem_Diff.xml", "Expected_SourceSystem_Diff.xml", false)]
    [DataRow("Actual_Same.xml", "Expected_Same.xml", true)]
    public async Task CompareXmlFilesAsync_WithSpecificComplexModelFiles_ShouldDetectDifferences(
        string actualFileName,
        string expectedFileName,
        bool expectEqual)
    {
        var testRoot = GetSpecificComplexModelTestRoot();
        var actualPath = Path.Combine(testRoot, "Actual", actualFileName);
        var expectedPath = Path.Combine(testRoot, "Expected", expectedFileName);

        actualPath.Should().MatchRegex(@".*Actual\\.+\.xml$");
        expectedPath.Should().MatchRegex(@".*Expected\\.+\.xml$");

        using var actualStream = File.OpenRead(actualPath);
        using var expectedStream = File.OpenRead(expectedPath);

        var result = await comparisonService.CompareXmlFilesAsync(
            actualStream,
            expectedStream,
            "ComplexOrderResponse");

        result.Should().NotBeNull();
        if (expectEqual)
        {
            result.AreEqual.Should().BeTrue();
            result.Differences.Should().BeEmpty();
        }
        else
        {
            result.AreEqual.Should().BeFalse();
            result.Differences.Should().NotBeEmpty();
        }
    }

    private static string GetSpecificComplexModelTestRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);

        while (current != null && !File.Exists(Path.Combine(current.FullName, "ComparisonTool.sln")))
        {
            current = current.Parent;
        }

        if (current == null)
        {
            throw new DirectoryNotFoundException("Could not locate ComparisonTool.sln to resolve test data paths.");
        }

        return Path.Combine(
            current.FullName,
            "ComparisonTool.Domain",
            "TestFiles",
            "SpecificTests_ComplexModel");
    }

    [DataTestMethod]
    [DataRow("Actual_MalformedXml.xml", "Expected_MalformedXml.xml", "Malformed XML with unclosed tags")]
    [DataRow("Actual_TruncatedXml.xml", "Expected_TruncatedXml.xml", "Truncated XML cut off mid-element")]
    [DataRow("Actual_EmptyFile.xml", "Expected_EmptyFile.xml", "Empty file with no content")]
    [DataRow("Actual_WrongRootElement.xml", "Expected_WrongRootElement.xml", "Wrong root element / different schema")]
    [DataRow("Actual_FaultException.xml", "Expected_FaultException.xml", "SOAP fault exception response instead of expected data")]
    public async Task CompareXmlFilesAsync_WithErrorScenarioFiles_ShouldThrowOnDeserialization(
        string actualFileName,
        string expectedFileName,
        string scenarioDescription)
    {
        // These file pairs exercise scenarios where the Actual side has content that
        // cannot be deserialized as a ComplexOrderResponse. In the File/Folder Comparison
        // UI, these errors are caught by DirectoryComparisonService and rendered via
        // ErrorDetailView.razor. At the ComparisonService level, they throw.
        var testRoot = GetSpecificComplexModelTestRoot();
        var actualPath = Path.Combine(testRoot, "Actual", actualFileName);
        var expectedPath = Path.Combine(testRoot, "Expected", expectedFileName);

        File.Exists(actualPath).Should().BeTrue($"Actual file should exist for scenario: {scenarioDescription}");
        File.Exists(expectedPath).Should().BeTrue($"Expected file should exist for scenario: {scenarioDescription}");

        using var actualStream = File.OpenRead(actualPath);
        using var expectedStream = File.OpenRead(expectedPath);

        var action = () => comparisonService.CompareXmlFilesAsync(
            actualStream,
            expectedStream,
            "ComplexOrderResponse");

        await action.Should().ThrowAsync<Exception>(
            $"Deserialization should fail for scenario: {scenarioDescription}");
    }

    [TestMethod]
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
        var action = () => comparisonService.CompareXmlFilesAsync(stream1, stream2, "UnregisteredModel");
        await action.Should().ThrowAsync<ArgumentException>();
    }

    [TestMethod]
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
        var action = () => comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");
        await action.Should().ThrowAsync<System.Reflection.TargetInvocationException>(); // Exception is wrapped when called through reflection
    }

    [TestMethod]
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
        var result1 = await comparisonService.CompareXmlFilesWithCachingAsync(
            stream1, stream2, "TestModel", "file1.xml", "file2.xml");

        // Reset streams for second comparison
        stream1.Position = 0;
        stream2.Position = 0;

        // Act - Second comparison (should use cache)
        var result2 = await comparisonService.CompareXmlFilesWithCachingAsync(
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
        public string? StringProperty
        {
            get; set;
        }

        [System.Xml.Serialization.XmlElement("IntProperty")]
        public int IntProperty
        {
            get; set;
        }
    }

    [System.Xml.Serialization.XmlRoot("ComplexTestModel")]
    public class ComplexTestModel
    {
        [System.Xml.Serialization.XmlElement("Name")]
        public string? Name
        {
            get; set;
        }

        [System.Xml.Serialization.XmlArray("Items")]
        [System.Xml.Serialization.XmlArrayItem("ComplexTestModelItem")]
        public List<ComplexTestModelItem> Items { get; set; } = new List<ComplexTestModelItem>();
    }

    public class ComplexTestModelItem
    {
        [System.Xml.Serialization.XmlElement("Id")]
        public int Id
        {
            get; set;
        }

        [System.Xml.Serialization.XmlElement("Value")]
        public string? Value
        {
            get; set;
        }
    }
}
