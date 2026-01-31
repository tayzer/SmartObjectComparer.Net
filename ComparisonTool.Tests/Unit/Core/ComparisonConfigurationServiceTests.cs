// <copyright file="ComparisonConfigurationServiceTests.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text.Json;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using FluentAssertions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class ComparisonConfigurationServiceTests
{
    private readonly Mock<ILogger<ComparisonConfigurationService>> mockLogger;
    private readonly ComparisonConfigurationOptions options;
    private readonly ComparisonConfigurationService service;

    public ComparisonConfigurationServiceTests()
    {
        mockLogger = new Mock<ILogger<ComparisonConfigurationService>>();
        options = new ComparisonConfigurationOptions
        {
            MaxDifferences = 1000,
            DefaultIgnoreCollectionOrder = true,
            DefaultIgnoreStringCase = false,
        };

        service = new ComparisonConfigurationService(mockLogger.Object, Options.Create(options));
    }

    [TestMethod]
    public void Constructor_WithValidOptions_ShouldInitializeCorrectly()
    {
        // Act & Assert
        service.Should().NotBeNull();
        service.GetCurrentConfig().Should().NotBeNull();
        service.GetCurrentConfig().MaxDifferences.Should().Be(1000);
        service.GetCurrentConfig().IgnoreCollectionOrder.Should().BeTrue();
        service.GetCurrentConfig().CaseSensitive.Should().BeTrue(); // DefaultIgnoreStringCase = false
    }

    [TestMethod]
    public void GetCompareLogic_ShouldReturnValidInstance()
    {
        // Act
        var compareLogic = service.GetCompareLogic();

        // Assert
        compareLogic.Should().NotBeNull();
        compareLogic.Config.Should().NotBeNull();
        compareLogic.Config.MaxDifferences.Should().Be(1000);
    }

    [TestMethod]
    public void GetThreadSafeCompareLogic_ShouldReturnIsolatedInstance()
    {
        // Act
        var compareLogic1 = service.GetThreadSafeCompareLogic();
        var compareLogic2 = service.GetThreadSafeCompareLogic();

        // Assert
        compareLogic1.Should().NotBeNull();
        compareLogic2.Should().NotBeNull();
        compareLogic1.Should().NotBeSameAs(compareLogic2);
    }

    [TestMethod]
    public void SetIgnoreCollectionOrder_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = service.GetIgnoreCollectionOrder();

        // Act
        service.SetIgnoreCollectionOrder(!originalValue);

        // Assert
        service.GetIgnoreCollectionOrder().Should().Be(!originalValue);
        service.GetCurrentConfig().IgnoreCollectionOrder.Should().Be(!originalValue);
    }

    [TestMethod]
    public void SetIgnoreStringCase_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = service.GetIgnoreStringCase();

        // Act
        service.SetIgnoreStringCase(!originalValue);

        // Assert
        service.GetIgnoreStringCase().Should().Be(!originalValue);
        service.GetCurrentConfig().CaseSensitive.Should().Be(originalValue); // CaseSensitive is inverse of IgnoreStringCase
    }

    [TestMethod]
    public void IgnoreProperty_WithValidPath_ShouldAddToIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";

        // Act
        service.IgnoreProperty(propertyPath);

        // Assert
        var ignoredProperties = service.GetIgnoredProperties();
        ignoredProperties.Should().Contain(propertyPath);
    }

    [TestMethod]
    public void RemoveIgnoredProperty_WithExistingProperty_ShouldRemoveFromIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";
        service.IgnoreProperty(propertyPath);

        // Act
        service.RemoveIgnoredProperty(propertyPath);

        // Assert
        var ignoredProperties = service.GetIgnoredProperties();
        ignoredProperties.Should().NotContain(propertyPath);
    }

    [TestMethod]
    public void AddIgnoreRule_WithValidRule_ShouldAddToRules()
    {
        // Arrange
        var rule = new IgnoreRule
        {
            PropertyPath = "TestProperty",
            IgnoreCollectionOrder = true,
        };

        // Act
        service.AddIgnoreRule(rule);

        // Assert
        var rules = service.GetIgnoreRules();
        rules.Should().ContainSingle();
        rules.First().PropertyPath.Should().Be("TestProperty");
    }

    [TestMethod]
    public void AddIgnoreRulesBatch_WithMultipleRules_ShouldAddAllRules()
    {
        // Arrange
        var rules = new List<IgnoreRule>
        {
            new () { PropertyPath = "Property1" },
            new () { PropertyPath = "Property2" },
            new () { PropertyPath = "Property3" },
        };

        // Act
        service.AddIgnoreRulesBatch(rules);

        // Assert
        var resultRules = service.GetIgnoreRules();
        resultRules.Should().HaveCount(3);
        resultRules.Should().Contain(r => r.PropertyPath == "Property1");
        resultRules.Should().Contain(r => r.PropertyPath == "Property2");
        resultRules.Should().Contain(r => r.PropertyPath == "Property3");
    }

    [TestMethod]
    public void ClearIgnoreRules_ShouldRemoveAllRules()
    {
        // Arrange
        service.AddIgnoreRule(new IgnoreRule { PropertyPath = "TestProperty" });

        // Act
        service.ClearIgnoreRules();

        // Assert
        var rules = service.GetIgnoreRules();
        rules.Should().BeEmpty();
    }

    [TestMethod]
    public void ApplyConfiguredSettings_ShouldApplyAllRules()
    {
        // Arrange
        service.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "TestProperty",
            IgnoreCollectionOrder = true,
        });

        // Act
        service.ApplyConfiguredSettings();

        // Assert
        // The configuration should be applied - we can verify by checking that the settings are reflected
        // in the compare logic configuration
        var compareLogic = service.GetCompareLogic();
        compareLogic.Config.Should().NotBeNull();
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithIgnoredProperty_ShouldFilterOutDifferences()
    {
        // Arrange
        var config = service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" },
            },
        };

        service.IgnoreProperty("TestProperty");

        // Act
        var filteredResult = service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Should().HaveCount(1);
        filteredResult.Differences.First().PropertyName.Should().Be("OtherProperty");
    }

    [TestMethod]
    public void AddSmartIgnoreRule_WithValidRule_ShouldAddToSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByNamePattern("Test.*", "Test pattern rule");

        // Act
        service.AddSmartIgnoreRule(rule);

        // Assert
        var rules = service.GetSmartIgnoreRules();
        rules.Should().ContainSingle();
        rules.First().Value.Should().Be("Test.*");
    }

    [TestMethod]
    public void RemoveSmartIgnoreRule_WithExistingRule_ShouldRemoveFromSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByPropertyName("TestProperty");
        service.AddSmartIgnoreRule(rule);

        // Act
        service.RemoveSmartIgnoreRule(rule);

        // Assert
        var rules = service.GetSmartIgnoreRules();
        rules.Should().BeEmpty();
    }

    [TestMethod]
    public void ClearSmartIgnoreRules_ShouldRemoveAllSmartRules()
    {
        // Arrange
        service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property1"));
        service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property2"));

        // Act
        service.ClearSmartIgnoreRules();

        // Assert
        var rules = service.GetSmartIgnoreRules();
        rules.Should().BeEmpty();
    }

    [TestMethod]
    public void FilterSmartIgnoredDifferences_WithSmartRule_ShouldFilterCorrectly()
    {
        // Arrange
        var config = service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" },
            },
        };

        service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("TestProperty"));

        // Act
        var filteredResult = service.FilterSmartIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Should().HaveCount(1);
        filteredResult.Differences.First().PropertyName.Should().Be("OtherProperty");
    }

    [TestMethod]
    public void NormalizePropertyValues_WithValidObject_ShouldSetDefaultValues()
    {
        // Arrange
        var testObject = new TestClass { StringProperty = "Test", IntProperty = 42 };
        var propertyNames = new List<string> { "StringProperty", "IntProperty" };

        // Act
        service.NormalizePropertyValues(testObject, propertyNames);

        // Assert
        testObject.StringProperty.Should().Be(string.Empty); // NormalizePropertyValues sets strings to empty string, not null
        testObject.IntProperty.Should().Be(0);
    }

    [TestMethod]
    public void AddXmlIgnorePropertiesToIgnoreList_WithType_ShouldAddXmlIgnoreProperties()
    {
        // Act
        service.AddXmlIgnorePropertiesToIgnoreList(typeof(TestClassWithXmlIgnore));

        // Assert
        var ignoredProperties = service.GetIgnoredProperties();
        ignoredProperties.Should().Contain("IgnoredProperty");
    }

    // Test helper classes
    private class TestClass
    {
        public string? StringProperty
        {
            get; set;
        }

        public int IntProperty
        {
            get; set;
        }
    }

    private class TestClassWithXmlIgnore
    {
        public string? NormalProperty
        {
            get; set;
        }

        [System.Xml.Serialization.XmlIgnore]
        public string? IgnoredProperty
        {
            get; set;
        }
    }
}
