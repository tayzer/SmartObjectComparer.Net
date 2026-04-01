using System.IO;
using System.Text;
using System.Text.Json;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.DI;
using ComparisonTool.Core.Models;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using ComparisonTool.Domain.Models;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
            DefaultIgnoreTrailingWhitespaceAtEnd = false,
        };

        configService = new ComparisonConfigurationService(mockConfigLogger.Object, Options.Create(configOptions));

        var serializerFactory = new ComparisonTool.Core.Serialization.XmlSerializerFactory();
        serializerFactory.RegisterType<ComplexOrderResponse>(
            () => serializerFactory.CreateComplexOrderResponseSerializer());
        serializerFactory.RegisterType<SoapEnvelope>(
            () => serializerFactory.CreateNamespaceIgnorantSerializer<SoapEnvelope>("Envelope"));
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
        xmlService.RegisterDomainModel<SoapEnvelope>("SoapEnvelope");
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

    [TestMethod]
    public async Task CompareXmlFilesAsync_WithTrailingWhitespaceIgnored_ShouldTreatValuesAsEqual()
    {
        // Arrange
        var xml1 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Value</StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        var xml2 = @"<?xml version=""1.0"" encoding=""utf-8""?>
<TestModel>
    <StringProperty>Value   </StringProperty>
    <IntProperty>42</IntProperty>
</TestModel>";

        using var stream1 = new MemoryStream(Encoding.UTF8.GetBytes(xml1));
        using var stream2 = new MemoryStream(Encoding.UTF8.GetBytes(xml2));

        configService.SetIgnoreTrailingWhitespaceAtEnd(true);

        // Act
        var result = await comparisonService.CompareXmlFilesAsync(stream1, stream2, "TestModel");

        // Assert
        result.Should().NotBeNull();
        result.Differences.Should().BeEmpty();
        result.AreEqual.Should().BeTrue();
    }

    [TestMethod]
    public async Task CompareXmlFilesAsync_WhenConfigurationChanges_ShouldRefreshComparisonLogicForSoapCollections()
    {
        var testRoot = GetCollectionOrderingTestRoot();
        var actualPath = Path.Combine(testRoot, "Actuals", "OrderTest.xml");
        var expectedPath = Path.Combine(testRoot, "Expecteds", "OrderTest.xml");
        configService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");

        configService.SetIgnoreCollectionOrder(false);
        configService.SetIgnoreTrailingWhitespaceAtEnd(false);

        using var initialActualStream = File.OpenRead(actualPath);
        using var initialExpectedStream = File.OpenRead(expectedPath);

        var initialResult = await comparisonService.CompareXmlFilesAsync(
            initialActualStream,
            initialExpectedStream,
            "SoapEnvelope");

        initialResult.Should().NotBeNull();
        initialResult.AreEqual.Should().BeFalse();
        initialResult.Differences.Should().NotBeEmpty();

        configService.SetIgnoreCollectionOrder(true);
        configService.SetIgnoreTrailingWhitespaceAtEnd(true);

        using var refreshedActualStream = File.OpenRead(actualPath);
        using var refreshedExpectedStream = File.OpenRead(expectedPath);

        var refreshedResult = await comparisonService.CompareXmlFilesAsync(
            refreshedActualStream,
            refreshedExpectedStream,
            "SoapEnvelope");

        refreshedResult.Should().NotBeNull();
        refreshedResult.AreEqual.Should().BeTrue();
        refreshedResult.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareXmlFilesAsync_WithConfiguredDefaultsFromDi_ShouldHonorIgnoreOptions()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ComparisonSettings:MaxDifferences"] = "1000",
                ["ComparisonSettings:DefaultIgnoreCollectionOrder"] = "true",
                ["ComparisonSettings:DefaultIgnoreTrailingWhitespaceAtEnd"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXmlComparisonServices(
            configuration,
            options => options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var scopedComparisonService = scope.ServiceProvider.GetRequiredService<IComparisonService>();
        var scopedConfigService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();

        scopedConfigService.GetIgnoreCollectionOrder().Should().BeTrue();
        scopedConfigService.GetIgnoreTrailingWhitespaceAtEnd().Should().BeTrue();
        scopedConfigService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");

        var testRoot = GetCollectionOrderingTestRoot();
        var actualPath = Path.Combine(testRoot, "Actuals", "OrderTest.xml");
        var expectedPath = Path.Combine(testRoot, "Expecteds", "OrderTest.xml");

        using var actualStream = File.OpenRead(actualPath);
        using var expectedStream = File.OpenRead(expectedPath);

        var result = await scopedComparisonService.CompareXmlFilesAsync(
            actualStream,
            expectedStream,
            "SoapEnvelope");

        result.Should().NotBeNull();
        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareXmlFilesAsync_WithIgnoredAddressIdentifiers_ShouldTreatReorderedAddressesAsEqual()
    {
        var testRoot = GetCollectionOrderingTestRoot();
        var actualPath = Path.Combine(testRoot, "Actuals", "OrderTest.xml");
        var expectedPath = Path.Combine(testRoot, "Expecteds", "OrderTest.xml");

        configService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");
        configService.SetIgnoreCollectionOrder(true);
        configService.SetIgnoreTrailingWhitespaceAtEnd(true);

        using var actualStream = File.OpenRead(actualPath);
        using var expectedStream = File.OpenRead(expectedPath);

        var result = await comparisonService.CompareXmlFilesAsync(
            actualStream,
            expectedStream,
            "SoapEnvelope");

        result.Should().NotBeNull();
        result.AreEqual.Should().BeTrue();
        result.Differences.Should().BeEmpty();
    }

    [TestMethod]
    public async Task CompareFoldersInBatchesAsync_WhenConfigurationChanges_ShouldRefreshHighPerformancePipeline()
    {
        const int pairCount = 100;
        configService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");

        configService.SetIgnoreCollectionOrder(false);
        configService.SetIgnoreTrailingWhitespaceAtEnd(false);

        var tempRoot = CreateCollectionOrderingCopySet(pairCount, out var actualPaths, out var expectedPaths);

        try
        {
            var initialResult = await comparisonService.CompareFoldersInBatchesAsync(
                actualPaths,
                expectedPaths,
                "SoapEnvelope",
                batchSize: 25);

            initialResult.TotalPairsCompared.Should().Be(pairCount);
            initialResult.AllEqual.Should().BeFalse();
            initialResult.FilePairResults.Should().HaveCount(pairCount);
            initialResult.FilePairResults.Should().OnlyContain(result => !result.AreEqual);

            configService.SetIgnoreCollectionOrder(true);
            configService.SetIgnoreTrailingWhitespaceAtEnd(true);

            var refreshedResult = await comparisonService.CompareFoldersInBatchesAsync(
                actualPaths,
                expectedPaths,
                "SoapEnvelope",
                batchSize: 25);

            refreshedResult.TotalPairsCompared.Should().Be(pairCount);
            refreshedResult.AllEqual.Should().BeTrue();
            refreshedResult.FilePairResults.Should().HaveCount(pairCount);
            refreshedResult.FilePairResults.Should().OnlyContain(result => result.AreEqual);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task AnalyzeStructualPatternsAsync_WhenResultMetadataSpecifiesIgnoreCollectionOrder_ShouldPreferMetadataOverCurrentConfigAsync()
    {
        configService.SetIgnoreCollectionOrder(false);

        var folderResult = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Actual.xml",
                    File2Name = "Expected.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            new Difference
                            {
                                PropertyName = "Items[0].Id",
                                Object1Value = "1",
                                Object2Value = "2",
                            },
                            new Difference
                            {
                                PropertyName = "Items[0].Value",
                                Object1Value = "A",
                                Object2Value = "B",
                            },
                            new Difference
                            {
                                PropertyName = "Items[1].Id",
                                Object1Value = "2",
                                Object2Value = "1",
                            },
                            new Difference
                            {
                                PropertyName = "Items[1].Value",
                                Object1Value = "B",
                                Object2Value = "A",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 4,
                    },
                },
            },
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["IgnoreCollectionOrder"] = true,
            },
        };

        var analysis = await comparisonService.AnalyzeStructualPatternsAsync(folderResult);

        analysis.ElementOrderDifferences.Should().BeEmpty();
        analysis.FileClassification.FileCounts["Order"].Should().Be(0);
        analysis.FileClassification.FileCounts["Value"].Should().Be(1);
        folderResult.Metadata["IgnoreCollectionOrder"].Should().Be(true);
    }

    [TestMethod]
    public async Task AnalyzeStructualPatternsAsync_WhenResultMetadataDisablesIgnoreCollectionOrder_ShouldPreferMetadataOverCurrentConfigAsync()
    {
        configService.SetIgnoreCollectionOrder(true);

        var folderResult = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 1,
            FilePairResults = new List<FilePairComparisonResult>
            {
                new()
                {
                    File1Name = "Actual.xml",
                    File2Name = "Expected.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            new Difference
                            {
                                PropertyName = "Items[0].Id",
                                Object1Value = "1",
                                Object2Value = "2",
                            },
                            new Difference
                            {
                                PropertyName = "Items[0].Value",
                                Object1Value = "A",
                                Object2Value = "B",
                            },
                            new Difference
                            {
                                PropertyName = "Items[1].Id",
                                Object1Value = "2",
                                Object2Value = "1",
                            },
                            new Difference
                            {
                                PropertyName = "Items[1].Value",
                                Object1Value = "B",
                                Object2Value = "A",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 4,
                    },
                },
            },
            Metadata = new Dictionary<string, object>(StringComparer.Ordinal)
            {
                ["IgnoreCollectionOrder"] = false,
            },
        };

        var analysis = await comparisonService.AnalyzeStructualPatternsAsync(folderResult);

        analysis.ElementOrderDifferences.Should().ContainSingle();
        analysis.FileClassification.FileCounts["Order"].Should().Be(1);
        analysis.FileClassification.FileCounts["Value"].Should().Be(0);
        folderResult.Metadata["IgnoreCollectionOrder"].Should().Be(false);
    }

    [TestMethod]
    public async Task AnalyzePatternsAsync_WhenDifferencePropertyNameIsNull_ShouldTreatItAsEmptyPathAsync()
    {
        var folderResult = new MultiFolderComparisonResult
        {
            AllEqual = false,
            TotalPairsCompared = 2,
            FilePairResults =
            [
                new()
                {
                    File1Name = "Actual1.xml",
                    File2Name = "Expected1.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            null!,
                            new Difference
                            {
                                PropertyName = null!,
                                Object1Value = "Old",
                                Object2Value = "New",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 2,
                    },
                },
                new()
                {
                    File1Name = "Actual2.xml",
                    File2Name = "Expected2.xml",
                    Result = new ComparisonResult(new ComparisonConfig())
                    {
                        Differences =
                        {
                            new Difference
                            {
                                PropertyName = null!,
                                Object1Value = "Old",
                                Object2Value = "New",
                            },
                        },
                    },
                    Summary = new DifferenceSummary
                    {
                        AreEqual = false,
                        TotalDifferenceCount = 1,
                    },
                },
            ],
        };

        var analysis = await comparisonService.AnalyzePatternsAsync(folderResult);

        analysis.TotalDifferences.Should().Be(3);
        analysis.FilesWithDifferences.Should().Be(2);
        analysis.CommonPathPatterns.Should().ContainSingle(pattern =>
            pattern.PatternPath == string.Empty &&
            pattern.FileCount == 2 &&
            pattern.OccurrenceCount == 2);
    }

    [TestMethod]
    public async Task CompareFoldersInBatchesAsync_WhenRunCompletes_ShouldWriteFlatDifferenceJsonLogAsync()
    {
        const string actualXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Example</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Alpha</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        const string expectedXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Example</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Beta</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

        var tempRoot = CreateComplexTestModelCopySet(1, actualXml, expectedXml, out var actualPaths, out var expectedPaths);

        try
        {
            var result = await comparisonService.CompareFoldersInBatchesAsync(actualPaths, expectedPaths, "ComplexTestModel", batchSize: 25);

            result.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);
            var outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;

            try
            {
                File.Exists(outputPath).Should().BeTrue();

                using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
                document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
                document.RootElement.GetArrayLength().Should().BeGreaterThan(0);
                document.RootElement[0].GetProperty("File1Name").GetString().Should().Be("000_ComplexTestModel.xml");
            }
            finally
            {
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

        [TestMethod]
        public async Task CompareFoldersInBatchesAsync_WhenHighPerformancePipelineRuns_ShouldMatchStandardDedupedDifferenceSet()
        {
                const int pairCount = 100;
                const string actualXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Example</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Alpha</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";
                const string expectedXml = @"<?xml version=""1.0"" encoding=""utf-8""?>
<ComplexTestModel>
    <Name>Example</Name>
    <Items>
        <ComplexTestModelItem>
            <Id>1</Id>
            <Value>Alpha</Value>
        </ComplexTestModelItem>
        <ComplexTestModelItem>
            <Id>2</Id>
            <Value>Beta</Value>
        </ComplexTestModelItem>
    </Items>
</ComplexTestModel>";

                using var actualStream = new MemoryStream(Encoding.UTF8.GetBytes(actualXml));
                using var expectedStream = new MemoryStream(Encoding.UTF8.GetBytes(expectedXml));

                var standardResult = await comparisonService.CompareXmlFilesAsync(
                        actualStream,
                        expectedStream,
                        "ComplexTestModel");

                var expectedDifferences = standardResult.Differences
                        .Select(CreateDifferenceSignature)
                        .ToList();

                expectedDifferences.Should().NotBeEmpty();
                expectedDifferences.Should().OnlyContain(signature =>
                        !signature.PropertyName.Contains("System.Collections.IList.Item", StringComparison.Ordinal));

                var tempRoot = CreateComplexTestModelCopySet(pairCount, actualXml, expectedXml, out var actualPaths, out var expectedPaths);

                try
                {
                        var batchResult = await comparisonService.CompareFoldersInBatchesAsync(
                                actualPaths,
                                expectedPaths,
                                "ComplexTestModel",
                                batchSize: 25);

                        batchResult.TotalPairsCompared.Should().Be(pairCount);
                        batchResult.FilePairResults.Should().HaveCount(pairCount);

                        foreach (var pair in batchResult.FilePairResults)
                        {
                                pair.HasError.Should().BeFalse();
                                pair.Result.Should().NotBeNull();

                                var actualDifferences = pair.Result!.Differences
                                        .Select(CreateDifferenceSignature)
                                        .ToList();

                                actualDifferences.Should().BeEquivalentTo(expectedDifferences);
                        }
                }
                finally
                {
                        if (Directory.Exists(tempRoot))
                        {
                                Directory.Delete(tempRoot, recursive: true);
                        }
                }
        }

    [TestMethod]
    public async Task CompareDirectoriesAsync_WhenLargeRunUsesHighPerformancePipeline_ShouldRespectConfigurationAndLogSessionResults()
    {
        const int pairCount = 100;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXmlComparisonServices(options =>
            options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var directoryComparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();
        var scopedConfigService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();
        var comparisonLogService = scope.ServiceProvider.GetRequiredService<IComparisonLogService>();

        scopedConfigService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");
        scopedConfigService.SetIgnoreCollectionOrder(false);
        scopedConfigService.SetIgnoreTrailingWhitespaceAtEnd(false);

        var tempRoot = CreateCollectionOrderingCopySet(pairCount, out _, out _);
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");
        string? initialOutputPath = null;
        string? refreshedOutputPath = null;

        try
        {
            var initialResult = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "SoapEnvelope");

            initialResult.TotalPairsCompared.Should().Be(pairCount);
            initialResult.AllEqual.Should().BeFalse();
            initialResult.FilePairResults.Should().HaveCount(pairCount);
            initialResult.FilePairResults.Should().OnlyContain(result => !result.AreEqual);
            initialResult.Metadata.Should().ContainKey("ComparisonSessionId");
            initialResult.Metadata.Should().ContainKey(ComparisonPhaseTimings.MetadataKey);
            initialResult.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);
            initialResult.Metadata.Should().ContainKey("PerformanceReportTextPath");
            initialResult.Metadata.Should().ContainKey("PerformanceReportCsvPath");

            initialOutputPath = initialResult.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;
            File.Exists(initialOutputPath).Should().BeTrue();

            var initialPhaseTimings = initialResult.Metadata[ComparisonPhaseTimings.MetadataKey]
                .Should().BeOfType<ComparisonPhaseTimings>().Which;
            initialPhaseTimings.XmlDeserializationPrecheckMs.Should().BePositive();
            initialPhaseTimings.XmlDeserializationFullDeserializeMs.Should().BePositive();
            initialPhaseTimings.CompareMs.Should().BePositive();
            initialPhaseTimings.FilterMs.Should().BePositive();
            initialPhaseTimings.ComparisonMs.Should().Be(initialPhaseTimings.CompareMs + initialPhaseTimings.FilterMs);

            var initialReportPath = initialResult.Metadata["PerformanceReportTextPath"].Should().BeOfType<string>().Which;
            var initialReportText = File.ReadAllText(initialReportPath);
            initialReportText.Should().Contain("HighPerfPipeline_ComparePair");
            initialReportText.Should().Contain("HighPerfPipeline_FilterPair");

            var initialSessionId = initialResult.Metadata["ComparisonSessionId"].Should().BeOfType<string>().Which;
            var initialSessionStats = comparisonLogService.GetSessionStats(initialSessionId);
            initialSessionStats.ProcessedFilePairs.Should().Be(pairCount);
            initialSessionStats.DifferentFilePairs.Should().Be(pairCount);
            initialSessionStats.ErrorFilePairs.Should().Be(0);

            scopedConfigService.SetIgnoreCollectionOrder(true);
            scopedConfigService.SetIgnoreTrailingWhitespaceAtEnd(true);

            var refreshedResult = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "SoapEnvelope");

            refreshedResult.TotalPairsCompared.Should().Be(pairCount);
            refreshedResult.AllEqual.Should().BeTrue();
            refreshedResult.FilePairResults.Should().HaveCount(pairCount);
            refreshedResult.FilePairResults.Should().OnlyContain(result => result.AreEqual);
            refreshedResult.Metadata.Should().ContainKey("ComparisonSessionId");
            refreshedResult.Metadata.Should().ContainKey(ComparisonPhaseTimings.MetadataKey);
            refreshedResult.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);

            refreshedOutputPath = refreshedResult.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;
            File.Exists(refreshedOutputPath).Should().BeTrue();

            var refreshedPhaseTimings = refreshedResult.Metadata[ComparisonPhaseTimings.MetadataKey]
                .Should().BeOfType<ComparisonPhaseTimings>().Which;
            refreshedPhaseTimings.XmlDeserializationPrecheckMs.Should().BeGreaterThanOrEqualTo(0);
            refreshedPhaseTimings.XmlDeserializationFullDeserializeMs.Should().BeGreaterThanOrEqualTo(0);
            (refreshedPhaseTimings.CollectionOrderDeterministicOrderingMs + refreshedPhaseTimings.CollectionOrderFallbackMs)
                .Should().BePositive();
            refreshedPhaseTimings.CollectionOrderFallbackCount.Should().BeGreaterThanOrEqualTo(0);
            AssertSupplementalMetricsPersisted(refreshedResult, refreshedPhaseTimings);

            var refreshedSessionId = refreshedResult.Metadata["ComparisonSessionId"].Should().BeOfType<string>().Which;
            var refreshedSessionStats = comparisonLogService.GetSessionStats(refreshedSessionId);
            refreshedSessionStats.ProcessedFilePairs.Should().Be(pairCount);
            refreshedSessionStats.EqualFilePairs.Should().Be(pairCount);
            refreshedSessionStats.DifferentFilePairs.Should().Be(0);
            refreshedSessionStats.ErrorFilePairs.Should().Be(0);
        }
        finally
        {
            if (initialOutputPath != null && File.Exists(initialOutputPath))
            {
                File.Delete(initialOutputPath);
            }

            if (refreshedOutputPath != null && File.Exists(refreshedOutputPath))
            {
                File.Delete(refreshedOutputPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CompareDirectoriesAsync_WhenStandardRunUsesComparisonEngine_ShouldRecordSplitPhaseTimings()
    {
        const int pairCount = 25;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXmlComparisonServices(options =>
            options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var directoryComparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();
        var scopedConfigService = scope.ServiceProvider.GetRequiredService<IComparisonConfigurationService>();

        scopedConfigService.IgnoreProperty("Body.Response.AddressLinks.Addresses[*].Id");
        scopedConfigService.SetIgnoreCollectionOrder(false);
        scopedConfigService.SetIgnoreTrailingWhitespaceAtEnd(true);
        scopedConfigService.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "Body.Response.AddressLinks.Addresses",
            IgnoreCollectionOrder = true,
        });

        var tempRoot = CreateCollectionOrderingCopySet(pairCount, out _, out _);
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");

        string? outputPath = null;

        try
        {
            var result = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "SoapEnvelope");

            result.TotalPairsCompared.Should().Be(pairCount);
            result.Metadata.Should().ContainKey(ComparisonPhaseTimings.MetadataKey);
            result.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);

            outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;
            File.Exists(outputPath).Should().BeTrue();

            var phaseTimings = result.Metadata[ComparisonPhaseTimings.MetadataKey]
                .Should().BeOfType<ComparisonPhaseTimings>().Which;

            phaseTimings.XmlDeserializationPrecheckMs.Should().BeGreaterThanOrEqualTo(0);
            phaseTimings.XmlDeserializationFullDeserializeMs.Should().BeGreaterThanOrEqualTo(0);
            phaseTimings.CompareMs.Should().BePositive();
            phaseTimings.FilterMs.Should().BeGreaterThanOrEqualTo(0);
            phaseTimings.ComparisonMs.Should().Be(phaseTimings.CompareMs + phaseTimings.FilterMs);
            phaseTimings.CollectionOrderDeterministicOrderingMs.Should().BeGreaterThanOrEqualTo(0);
            phaseTimings.CollectionOrderFallbackMs.Should().BeGreaterThanOrEqualTo(0);
            phaseTimings.CollectionOrderFallbackCount.Should().BeGreaterThanOrEqualTo(0);
            AssertSupplementalMetricsPersisted(result, phaseTimings);
        }
        finally
        {
            if (outputPath != null && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CompareDirectoriesAsync_WhenNoPairsExist_ShouldStillWriteEmptyFlatDifferenceJsonLogAsync()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXmlComparisonServices(options =>
            options.RegisterDomainModelWithRootElement<SoapEnvelope>("SoapEnvelope", "Envelope"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var directoryComparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

        var tempRoot = Path.Combine(Path.GetTempPath(), $"ComparisonTool_Empty_{Guid.NewGuid():N}");
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");
        Directory.CreateDirectory(actualDirectory);
        Directory.CreateDirectory(expectedDirectory);

        string? outputPath = null;

        try
        {
            var result = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "SoapEnvelope");

            result.AllEqual.Should().BeTrue();
            result.TotalPairsCompared.Should().Be(0);
            result.FilePairResults.Should().BeEmpty();
            result.Metadata.Should().ContainKey(FlatDifferenceJsonLogWriter.MetadataKey);

            outputPath = result.Metadata[FlatDifferenceJsonLogWriter.MetadataKey].Should().BeOfType<string>().Which;
            File.Exists(outputPath).Should().BeTrue();

            using var document = JsonDocument.Parse(File.ReadAllText(outputPath));
            document.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
            document.RootElement.GetArrayLength().Should().Be(0);
        }
        finally
        {
            if (outputPath != null && File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    [TestMethod]
    public async Task CompareDirectoriesAsync_WhenLargeJsonRunUsesHighPerformancePipeline_ShouldCompareJsonFilesSuccessfully()
    {
        const int pairCount = 100;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddUnifiedComparisonServices(options =>
            options.RegisterDomainModel<CustomerOrder>("CustomerOrder"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var directoryComparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();
        var comparisonLogService = scope.ServiceProvider.GetRequiredService<IComparisonLogService>();

        var tempRoot = CreateCustomerOrderJsonCopySet(pairCount);
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");

        try
        {
            var result = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "CustomerOrder",
                includeAllFiles: true);

            result.TotalPairsCompared.Should().Be(pairCount);
            result.AllEqual.Should().BeFalse();
            result.FilePairResults.Should().HaveCount(pairCount);
            result.FilePairResults.Should().OnlyContain(fileResult => !fileResult.AreEqual && !fileResult.HasError);
            result.Metadata.Should().ContainKey("ComparisonSessionId");

            var sessionId = result.Metadata["ComparisonSessionId"].Should().BeOfType<string>().Which;
            var sessionStats = comparisonLogService.GetSessionStats(sessionId);
            sessionStats.ProcessedFilePairs.Should().Be(pairCount);
            sessionStats.DifferentFilePairs.Should().Be(pairCount);
            sessionStats.ErrorFilePairs.Should().Be(0);
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
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

    [TestMethod]
    public async Task CompareFoldersInBatchesAsync_WithSoapFaultFile_ShouldReturnErrorRowWithoutThrowing()
    {
        var testRoot = GetSpecificComplexModelTestRoot();
        var actualPath = Path.Combine(testRoot, "Actual", "Actual_FaultException.xml");
        var expectedPath = Path.Combine(testRoot, "Expected", "Expected_FaultException.xml");

        var result = await comparisonService.CompareFoldersInBatchesAsync(
            new List<string> { actualPath },
            new List<string> { expectedPath },
            "ComplexOrderResponse",
            batchSize: 25);

        result.TotalPairsCompared.Should().Be(1);
        result.AllEqual.Should().BeFalse();
        result.FilePairResults.Should().HaveCount(1);

        var pair = result.FilePairResults[0];
        pair.HasError.Should().BeTrue();
        pair.ErrorMessage.Should().Contain("SOAP fault detected in response");
        pair.ErrorMessage.Should().Contain("soap:Server");
    }

    [TestMethod]
    public async Task CompareDirectoriesAsync_WithSoapFaultFile_ShouldReturnErrorRowWithoutThrowing()
    {
        var testRoot = GetSpecificComplexModelTestRoot();
        var sourceActual = Path.Combine(testRoot, "Actual", "Actual_FaultException.xml");
        var sourceExpected = Path.Combine(testRoot, "Expected", "Expected_FaultException.xml");

        var tempRoot = Path.Combine(Path.GetTempPath(), "ComparisonToolFaultCopies", Guid.NewGuid().ToString("N"));
        var actualDirectory = Path.Combine(tempRoot, "Actual");
        var expectedDirectory = Path.Combine(tempRoot, "Expected");
        Directory.CreateDirectory(actualDirectory);
        Directory.CreateDirectory(expectedDirectory);

        var targetActual = Path.Combine(actualDirectory, "Fault.xml");
        var targetExpected = Path.Combine(expectedDirectory, "Fault.xml");
        File.Copy(sourceActual, targetActual);
        File.Copy(sourceExpected, targetExpected);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddXmlComparisonServices(options =>
            options.RegisterDomainModelWithRootElement<ComplexOrderResponse>("ComplexOrderResponse", "OrderManagementResponse"));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var directoryComparisonService = scope.ServiceProvider.GetRequiredService<DirectoryComparisonService>();

        try
        {
            var result = await directoryComparisonService.CompareDirectoriesAsync(
                actualDirectory,
                expectedDirectory,
                "ComplexOrderResponse");

            result.TotalPairsCompared.Should().Be(1);
            result.AllEqual.Should().BeFalse();
            result.FilePairResults.Should().HaveCount(1);

            var pair = result.FilePairResults[0];
            pair.HasError.Should().BeTrue();
            pair.ErrorMessage.Should().Contain("SOAP fault detected in response");
            pair.ErrorMessage.Should().Contain("order processing service encountered an internal error");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static string GetSpecificComplexModelTestRoot()
    {
        return Path.Combine(
            GetSolutionRoot(),
            "ComparisonTool.Domain",
            "TestFiles",
            "SpecificTests_ComplexModel");
    }

    private static string GetCollectionOrderingTestRoot()
    {
        return Path.Combine(
            GetSolutionRoot(),
            "ComparisonTool.Domain",
            "TestFiles",
            "CollectionOrdering");
    }

    private static string GetSolutionRoot()
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

        return current.FullName;
    }

    private static string CreateCollectionOrderingCopySet(
        int pairCount,
        out List<string> actualPaths,
        out List<string> expectedPaths)
    {
        var testRoot = GetCollectionOrderingTestRoot();
        var sourceActualPath = Path.Combine(testRoot, "Actuals", "OrderTest.xml");
        var sourceExpectedPath = Path.Combine(testRoot, "Expecteds", "OrderTest.xml");

        var tempRoot = Path.Combine(Path.GetTempPath(), "ComparisonToolTests", Guid.NewGuid().ToString("N"));
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");

        Directory.CreateDirectory(actualDirectory);
        Directory.CreateDirectory(expectedDirectory);

        actualPaths = new List<string>(pairCount);
        expectedPaths = new List<string>(pairCount);

        for (var index = 0; index < pairCount; index++)
        {
            var fileName = $"{index:D3}_OrderTest.xml";
            var actualPath = Path.Combine(actualDirectory, fileName);
            var expectedPath = Path.Combine(expectedDirectory, fileName);

            File.Copy(sourceActualPath, actualPath);
            File.Copy(sourceExpectedPath, expectedPath);

            actualPaths.Add(actualPath);
            expectedPaths.Add(expectedPath);
        }

        return tempRoot;
    }

    private static string CreateComplexTestModelCopySet(
        int pairCount,
        string actualXml,
        string expectedXml,
        out List<string> actualPaths,
        out List<string> expectedPaths)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ComparisonToolComplexTestModelCopies", Guid.NewGuid().ToString("N"));
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");

        Directory.CreateDirectory(actualDirectory);
        Directory.CreateDirectory(expectedDirectory);

        actualPaths = new List<string>(pairCount);
        expectedPaths = new List<string>(pairCount);

        for (var index = 0; index < pairCount; index++)
        {
            var fileName = $"{index:D3}_ComplexTestModel.xml";
            var actualPath = Path.Combine(actualDirectory, fileName);
            var expectedPath = Path.Combine(expectedDirectory, fileName);

            File.WriteAllText(actualPath, actualXml, Encoding.UTF8);
            File.WriteAllText(expectedPath, expectedXml, Encoding.UTF8);

            actualPaths.Add(actualPath);
            expectedPaths.Add(expectedPath);
        }

        return tempRoot;
    }

    private static (string PropertyName, string? OldValue, string? NewValue) CreateDifferenceSignature(Difference diff) =>
        (diff.PropertyName, diff.Object1Value?.ToString(), diff.Object2Value?.ToString());

    private static string CreateCustomerOrderJsonCopySet(int pairCount)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "ComparisonToolJsonCopies", Guid.NewGuid().ToString("N"));
        var actualDirectory = Path.Combine(tempRoot, "Actuals");
        var expectedDirectory = Path.Combine(tempRoot, "Expecteds");
        Directory.CreateDirectory(actualDirectory);
        Directory.CreateDirectory(expectedDirectory);

        var testRoot = Path.Combine(GetSolutionRoot(), "ComparisonTool.Domain", "TestFiles", "CustomerOrderTest");
        var sourceActual = Path.Combine(testRoot, "Actual", "Original.json");
        var sourceExpected = Path.Combine(testRoot, "Expected", "Modified.json");

        for (var index = 0; index < pairCount; index++)
        {
            var fileName = $"{index + 1}.json";
            File.Copy(sourceActual, Path.Combine(actualDirectory, fileName));
            File.Copy(sourceExpected, Path.Combine(expectedDirectory, fileName));
        }

        return tempRoot;
    }

    private static void AssertSupplementalMetricsPersisted(
        MultiFolderComparisonResult result,
        ComparisonPhaseTimings phaseTimings)
    {
        result.Metadata.Should().ContainKey("PerformanceReportTextPath");
        result.Metadata.Should().ContainKey("PerformanceReportCsvPath");

        var textReportPath = result.Metadata["PerformanceReportTextPath"].Should().BeOfType<string>().Which;
        var textReport = File.ReadAllText(textReportPath);
        textReport.Should().MatchRegex("(?s)Operation: .*SUPPLEMENTAL METRICS");
        textReport.Should().Contain($"XmlDeserializationPrecheckMs: {phaseTimings.XmlDeserializationPrecheckMs}");
        textReport.Should().Contain($"XmlDeserializationFullDeserializeMs: {phaseTimings.XmlDeserializationFullDeserializeMs}");
        textReport.Should().Contain($"CollectionOrderDeterministicOrderingMs: {phaseTimings.CollectionOrderDeterministicOrderingMs}");
        textReport.Should().Contain($"CollectionOrderFallbackMs: {phaseTimings.CollectionOrderFallbackMs}");
        textReport.Should().Contain($"CollectionOrderFallbackCount: {phaseTimings.CollectionOrderFallbackCount}");

        var csvReportPath = result.Metadata["PerformanceReportCsvPath"].Should().BeOfType<string>().Which;
        var csvLines = File.ReadAllLines(csvReportPath);
        var blankLineIndex = Array.IndexOf(csvLines, string.Empty);

        csvLines[0].Should().Be("Operation,CallCount,TotalTimeMs,AverageTimeMs,MedianTimeMs,MinTimeMs,MaxTimeMs");
        blankLineIndex.Should().BeGreaterThan(0);
        csvLines[blankLineIndex + 1].Should().Be("Metric,Value");
        csvLines.Should().Contain($"XmlDeserializationPrecheckMs,{phaseTimings.XmlDeserializationPrecheckMs}");
        csvLines.Should().Contain($"XmlDeserializationFullDeserializeMs,{phaseTimings.XmlDeserializationFullDeserializeMs}");
        csvLines.Should().Contain($"CollectionOrderDeterministicOrderingMs,{phaseTimings.CollectionOrderDeterministicOrderingMs}");
        csvLines.Should().Contain($"CollectionOrderFallbackMs,{phaseTimings.CollectionOrderFallbackMs}");
        csvLines.Should().Contain($"CollectionOrderFallbackCount,{phaseTimings.CollectionOrderFallbackCount}");
    }

    [DataTestMethod]
    //[DataRow("Actual_MalformedXml.xml", "Expected_MalformedXml.xml", "Malformed XML with unclosed tags")]
    //[DataRow("Actual_TruncatedXml.xml", "Expected_TruncatedXml.xml", "Truncated XML cut off mid-element")]
    //[DataRow("Actual_EmptyFile.xml", "Expected_EmptyFile.xml", "Empty file with no content")]
    //[DataRow("Actual_WrongRootElement.xml", "Expected_WrongRootElement.xml", "Wrong root element / different schema")]
    [DataRow("Actual_FaultException.xml", "Expected_FaultException.xml", "SOAP fault exception response instead of expected data")]
    public async Task CompareXmlFilesAsync_WithErrorScenarioFiles_ShouldThrowOnDeserialization(
        string actualFileName,
        string expectedFileName,
        string scenarioDescription)
    {
        // These file pairs exercise scenarios where the Actual side has content that
        // cannot be deserialized as a ComplexOrderResponse. Deserialization uses
        // TryDeserializeXml which pre-validates the root element, catching SOAP faults
        // and wrong schemas WITHOUT throwing from XmlSerializer.Deserialize().
        // The pre-validation failure is returned as a DeserializationResult.Failure,
        // then the orchestrator wraps it in an InvalidOperationException for callers
        // that expect exceptions (like this test). In the File/Folder Comparison UI,
        // errors are caught by DirectoryComparisonService and rendered via ErrorDetailView.razor.
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
        await action.Should().ThrowAsync<InvalidOperationException>(); // TryDeserializeXml catches the XML parsing error internally and returns Failure; orchestrator wraps in InvalidOperationException
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
