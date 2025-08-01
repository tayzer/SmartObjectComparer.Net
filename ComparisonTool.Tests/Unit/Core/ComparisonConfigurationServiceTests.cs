using System.Text.Json;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace ComparisonTool.Tests.Unit.Core;

public class ComparisonConfigurationServiceTests
{
    private readonly Mock<ILogger<ComparisonConfigurationService>> _mockLogger;
    private readonly ComparisonConfigurationOptions _options;
    private readonly ComparisonConfigurationService _service;

    public ComparisonConfigurationServiceTests()
    {
        _mockLogger = new Mock<ILogger<ComparisonConfigurationService>>();
        _options = new ComparisonConfigurationOptions
        {
            MaxDifferences = 1000,
            DefaultIgnoreCollectionOrder = true,
            DefaultIgnoreStringCase = false
        };
        
        _service = new ComparisonConfigurationService(_mockLogger.Object, Options.Create(_options));
    }

    [Fact]
    public void Constructor_WithValidOptions_ShouldInitializeCorrectly()
    {
        // Act & Assert
        _service.Should().NotBeNull();
        _service.GetCurrentConfig().Should().NotBeNull();
        _service.GetCurrentConfig().MaxDifferences.Should().Be(1000);
        _service.GetCurrentConfig().IgnoreCollectionOrder.Should().BeTrue();
        _service.GetCurrentConfig().CaseSensitive.Should().BeTrue(); // DefaultIgnoreStringCase = false
    }

    [Fact]
    public void GetCompareLogic_ShouldReturnValidInstance()
    {
        // Act
        var compareLogic = _service.GetCompareLogic();

        // Assert
        compareLogic.Should().NotBeNull();
        compareLogic.Config.Should().NotBeNull();
        compareLogic.Config.MaxDifferences.Should().Be(1000);
    }

    [Fact]
    public void GetThreadSafeCompareLogic_ShouldReturnIsolatedInstance()
    {
        // Act
        var compareLogic1 = _service.GetThreadSafeCompareLogic();
        var compareLogic2 = _service.GetThreadSafeCompareLogic();

        // Assert
        compareLogic1.Should().NotBeNull();
        compareLogic2.Should().NotBeNull();
        compareLogic1.Should().NotBeSameAs(compareLogic2);
    }

    [Fact]
    public void SetIgnoreCollectionOrder_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = _service.GetIgnoreCollectionOrder();

        // Act
        _service.SetIgnoreCollectionOrder(!originalValue);

        // Assert
        _service.GetIgnoreCollectionOrder().Should().Be(!originalValue);
        _service.GetCurrentConfig().IgnoreCollectionOrder.Should().Be(!originalValue);
    }

    [Fact]
    public void SetIgnoreStringCase_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = _service.GetIgnoreStringCase();

        // Act
        _service.SetIgnoreStringCase(!originalValue);

        // Assert
        _service.GetIgnoreStringCase().Should().Be(!originalValue);
        _service.GetCurrentConfig().CaseSensitive.Should().Be(originalValue); // CaseSensitive is inverse of IgnoreStringCase
    }

    [Fact]
    public void IgnoreProperty_WithValidPath_ShouldAddToIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";

        // Act
        _service.IgnoreProperty(propertyPath);

        // Assert
        var ignoredProperties = _service.GetIgnoredProperties();
        ignoredProperties.Should().Contain(propertyPath);
    }

    [Fact]
    public void RemoveIgnoredProperty_WithExistingProperty_ShouldRemoveFromIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";
        _service.IgnoreProperty(propertyPath);

        // Act
        _service.RemoveIgnoredProperty(propertyPath);

        // Assert
        var ignoredProperties = _service.GetIgnoredProperties();
        ignoredProperties.Should().NotContain(propertyPath);
    }

    [Fact]
    public void AddIgnoreRule_WithValidRule_ShouldAddToRules()
    {
        // Arrange
        var rule = new IgnoreRule
        {
            PropertyPath = "TestProperty",
            IgnoreCollectionOrder = true
        };

        // Act
        _service.AddIgnoreRule(rule);

        // Assert
        var rules = _service.GetIgnoreRules();
        rules.Should().ContainSingle();
        rules.First().PropertyPath.Should().Be("TestProperty");
    }

    [Fact]
    public void AddIgnoreRulesBatch_WithMultipleRules_ShouldAddAllRules()
    {
        // Arrange
        var rules = new List<IgnoreRule>
        {
            new() { PropertyPath = "Property1" },
            new() { PropertyPath = "Property2" },
            new() { PropertyPath = "Property3" }
        };

        // Act
        _service.AddIgnoreRulesBatch(rules);

        // Assert
        var resultRules = _service.GetIgnoreRules();
        resultRules.Should().HaveCount(3);
        resultRules.Should().Contain(r => r.PropertyPath == "Property1");
        resultRules.Should().Contain(r => r.PropertyPath == "Property2");
        resultRules.Should().Contain(r => r.PropertyPath == "Property3");
    }

    [Fact]
    public void ClearIgnoreRules_ShouldRemoveAllRules()
    {
        // Arrange
        _service.AddIgnoreRule(new IgnoreRule { PropertyPath = "TestProperty" });

        // Act
        _service.ClearIgnoreRules();

        // Assert
        var rules = _service.GetIgnoreRules();
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ApplyConfiguredSettings_ShouldApplyAllRules()
    {
        // Arrange
        _service.AddIgnoreRule(new IgnoreRule 
        { 
            PropertyPath = "TestProperty",
            IgnoreCollectionOrder = true
        });

        // Act
        _service.ApplyConfiguredSettings();

        // Assert
        // The configuration should be applied - we can verify by checking that the settings are reflected
        // in the compare logic configuration
        var compareLogic = _service.GetCompareLogic();
        compareLogic.Config.Should().NotBeNull();
    }

    [Fact]
    public void FilterIgnoredDifferences_WithIgnoredProperty_ShouldFilterOutDifferences()
    {
        // Arrange
        var config = _service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new() { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new() { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" }
            }
        };

        _service.IgnoreProperty("TestProperty");

        // Act
        var filteredResult = _service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Should().HaveCount(1);
        filteredResult.Differences.First().PropertyName.Should().Be("OtherProperty");
    }

    [Fact]
    public void AddSmartIgnoreRule_WithValidRule_ShouldAddToSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByNamePattern("Test.*", "Test pattern rule");

        // Act
        _service.AddSmartIgnoreRule(rule);

        // Assert
        var rules = _service.GetSmartIgnoreRules();
        rules.Should().ContainSingle();
        rules.First().Value.Should().Be("Test.*");
    }

    [Fact]
    public void RemoveSmartIgnoreRule_WithExistingRule_ShouldRemoveFromSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByPropertyName("TestProperty");
        _service.AddSmartIgnoreRule(rule);

        // Act
        _service.RemoveSmartIgnoreRule(rule);

        // Assert
        var rules = _service.GetSmartIgnoreRules();
        rules.Should().BeEmpty();
    }

    [Fact]
    public void ClearSmartIgnoreRules_ShouldRemoveAllSmartRules()
    {
        // Arrange
        _service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property1"));
        _service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property2"));

        // Act
        _service.ClearSmartIgnoreRules();

        // Assert
        var rules = _service.GetSmartIgnoreRules();
        rules.Should().BeEmpty();
    }

    [Fact]
    public void FilterSmartIgnoredDifferences_WithSmartRule_ShouldFilterCorrectly()
    {
        // Arrange
        var config = _service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new() { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new() { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" }
            }
        };

        _service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("TestProperty"));

        // Act
        var filteredResult = _service.FilterSmartIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Should().HaveCount(1);
        filteredResult.Differences.First().PropertyName.Should().Be("OtherProperty");
    }

    [Fact]
    public void NormalizePropertyValues_WithValidObject_ShouldSetDefaultValues()
    {
        // Arrange
        var testObject = new TestClass { StringProperty = "Test", IntProperty = 42 };
        var propertyNames = new List<string> { "StringProperty", "IntProperty" };

        // Act
        _service.NormalizePropertyValues(testObject, propertyNames);

        // Assert
        testObject.StringProperty.Should().Be(""); // NormalizePropertyValues sets strings to empty string, not null
        testObject.IntProperty.Should().Be(0);
    }

    [Fact]
    public void AddXmlIgnorePropertiesToIgnoreList_WithType_ShouldAddXmlIgnoreProperties()
    {
        // Act
        _service.AddXmlIgnorePropertiesToIgnoreList(typeof(TestClassWithXmlIgnore));

        // Assert
        var ignoredProperties = _service.GetIgnoredProperties();
        ignoredProperties.Should().Contain("IgnoredProperty");
    }

    // Test helper classes
    private class TestClass
    {
        public string? StringProperty { get; set; }
        public int IntProperty { get; set; }
    }

    private class TestClassWithXmlIgnore
    {
        public string? NormalProperty { get; set; }
        
        [System.Xml.Serialization.XmlIgnore]
        public string? IgnoredProperty { get; set; }
    }
} 