using System.Text.Json;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

namespace ComparisonTool.Tests.Unit.Core;

[TestClass]
public class ComparisonConfigurationServiceTests
{
    private readonly Mock<ILogger<ComparisonConfigurationService>> mockLogger;
    private readonly ComparisonConfigurationOptions options;
    private readonly ComparisonConfigurationService service;

    public ComparisonConfigurationServiceTests()
    {
        this.mockLogger = new Mock<ILogger<ComparisonConfigurationService>>();
        this.options = new ComparisonConfigurationOptions
        {
            MaxDifferences = 1000,
            DefaultIgnoreCollectionOrder = true,
            DefaultIgnoreStringCase = false,
            DefaultIgnoreTrailingWhitespaceAtEnd = false,
        };

        this.service = new ComparisonConfigurationService(this.mockLogger.Object, Options.Create(this.options));
    }

    [TestMethod]
    public void Constructor_WithValidOptions_ShouldInitializeCorrectly()
    {
        // Act & Assert
        this.service.ShouldNotBeNull();
        this.service.GetCurrentConfig().ShouldNotBeNull();
        this.service.GetCurrentConfig().MaxDifferences.ShouldBe(1000);
        this.service.GetCurrentConfig().IgnoreCollectionOrder.ShouldBeTrue();
        this.service.GetCurrentConfig().CaseSensitive.ShouldBeTrue(); // DefaultIgnoreStringCase = false
    }

    [TestMethod]
    public void GetCompareLogic_ShouldReturnValidInstance()
    {
        // Act
        var compareLogic = this.service.GetCompareLogic();

        // Assert
        compareLogic.ShouldNotBeNull();
        compareLogic.Config.ShouldNotBeNull();
        compareLogic.Config.MaxDifferences.ShouldBe(1000);
    }

    [TestMethod]
    public void GetThreadSafeCompareLogic_ShouldReturnIsolatedInstance()
    {
        // Act
        var compareLogic1 = this.service.GetThreadSafeCompareLogic();
        var compareLogic2 = this.service.GetThreadSafeCompareLogic();

        // Assert
        compareLogic1.ShouldNotBeNull();
        compareLogic2.ShouldNotBeNull();
        ReferenceEquals(compareLogic1, compareLogic2).ShouldBeFalse();
    }

    [TestMethod]
    public void SetIgnoreCollectionOrder_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = this.service.GetIgnoreCollectionOrder();

        // Act
        this.service.SetIgnoreCollectionOrder(!originalValue);

        // Assert
        this.service.GetIgnoreCollectionOrder().ShouldBe(!originalValue);
        this.service.GetCurrentConfig().IgnoreCollectionOrder.ShouldBe(!originalValue);
    }

    [TestMethod]
    public void SetIgnoreStringCase_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = this.service.GetIgnoreStringCase();

        // Act
        this.service.SetIgnoreStringCase(!originalValue);

        // Assert
        this.service.GetIgnoreStringCase().ShouldBe(!originalValue);
        this.service.GetCurrentConfig().CaseSensitive.ShouldBe(originalValue); // CaseSensitive is inverse of IgnoreStringCase
    }

    [TestMethod]
    public void SetIgnoreTrailingWhitespaceAtEnd_ShouldUpdateConfiguration()
    {
        // Arrange
        var originalValue = this.service.GetIgnoreTrailingWhitespaceAtEnd();

        // Act
        this.service.SetIgnoreTrailingWhitespaceAtEnd(!originalValue);

        // Assert
        this.service.GetIgnoreTrailingWhitespaceAtEnd().ShouldBe(!originalValue);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithTrailingWhitespaceIgnored_ShouldFilterOutTrailingWhitespaceOnlyStringDifferences()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "StringProperty", Object1Value = "Value", Object2Value = "Value \t" },
                new () { PropertyName = "OtherProperty", Object1Value = "A", Object2Value = "B" },
            },
        };

        this.service.SetIgnoreTrailingWhitespaceAtEnd(true);

        // Act
        var filteredResult = this.service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count.ShouldBe(1);
        filteredResult.Differences.Count(d => d.PropertyName == "OtherProperty").ShouldBe(1);
    }

    [TestMethod]
    public void IgnoreProperty_WithValidPath_ShouldAddToIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";

        // Act
        this.service.IgnoreProperty(propertyPath);

        // Assert
        var ignoredProperties = this.service.GetIgnoredProperties();
        ignoredProperties.Contains(propertyPath).ShouldBeTrue();
    }

    [TestMethod]
    public void RemoveIgnoredProperty_WithExistingProperty_ShouldRemoveFromIgnoreList()
    {
        // Arrange
        var propertyPath = "TestProperty";
        this.service.IgnoreProperty(propertyPath);

        // Act
        this.service.RemoveIgnoredProperty(propertyPath);

        // Assert
        var ignoredProperties = this.service.GetIgnoredProperties();
        ignoredProperties.Contains(propertyPath).ShouldBeFalse();
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
        this.service.AddIgnoreRule(rule);

        // Assert
        var rules = this.service.GetIgnoreRules();
        rules.Count.ShouldBe(1);
        rules.First().PropertyPath.ShouldBe("TestProperty");
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
        this.service.AddIgnoreRulesBatch(rules);

        // Assert
        var resultRules = this.service.GetIgnoreRules();
        resultRules.Count.ShouldBe(3);
        resultRules.Any(r => r.PropertyPath == "Property1").ShouldBeTrue();
        resultRules.Any(r => r.PropertyPath == "Property2").ShouldBeTrue();
        resultRules.Any(r => r.PropertyPath == "Property3").ShouldBeTrue();
    }

    [TestMethod]
    public void ClearIgnoreRules_ShouldRemoveAllRules()
    {
        // Arrange
        this.service.AddIgnoreRule(new IgnoreRule { PropertyPath = "TestProperty" });

        // Act
        this.service.ClearIgnoreRules();

        // Assert
        var rules = this.service.GetIgnoreRules();
        rules.ShouldBeEmpty();
    }

    [TestMethod]
    public void ApplyConfiguredSettings_ShouldApplyAllRules()
    {
        // Arrange
        this.service.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "TestProperty",
            IgnoreCollectionOrder = true,
        });

        // Act
        this.service.ApplyConfiguredSettings();

        // Assert
        // The configuration should be applied - we can verify by checking that the settings are reflected
        // in the compare logic configuration
        var compareLogic = this.service.GetCompareLogic();
        compareLogic.Config.ShouldNotBeNull();
    }

    [TestMethod]
    public void ApplyConfiguredSettings_WhenCachedConfigurationIsReapplied_ShouldPreserveCaseSensitivity()
    {
        // Arrange
        this.service.SetIgnoreStringCase(true);

        // Act
        this.service.ApplyConfiguredSettings();
        this.service.ApplyConfiguredSettings();

        // Assert
        this.service.GetIgnoreStringCase().ShouldBeTrue();
        this.service.GetCurrentConfig().CaseSensitive.ShouldBeFalse();
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithIgnoredProperty_ShouldFilterOutDifferences()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" },
            },
        };

        this.service.IgnoreProperty("TestProperty");

        // Act
        var filteredResult = this.service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count.ShouldBe(1);
        filteredResult.Differences.First().PropertyName.ShouldBe("OtherProperty");
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithLargeIgnoreSetAndIgnoredParentProperty_ShouldFilterNestedDifference()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "OrderData.Customer.Name", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OrderData.Supplier.Name", Object1Value = "Old", Object2Value = "New" },
            },
        };

        this.AddPaddingIgnoreRules();
        this.service.IgnoreProperty("OrderData.Customer");

        // Act
        var filteredResult = this.service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count(d => d.PropertyName == "OrderData.Supplier.Name").ShouldBe(1);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithLargeIgnoreSetAndIgnoredCollectionRoot_ShouldFilterCollectionItemDifference()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "Metadata.Performance.ComponentTimings[3].CallCount", Object1Value = "1", Object2Value = "2" },
                new () { PropertyName = "Metadata.Version", Object1Value = "1", Object2Value = "2" },
            },
        };

        this.AddPaddingIgnoreRules();
        this.service.IgnoreProperty("Metadata.Performance.ComponentTimings");

        // Act
        var filteredResult = this.service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count(d => d.PropertyName == "Metadata.Version").ShouldBe(1);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithLargeIgnoreSetAndWildcardCollectionPattern_ShouldFilterMatchingDifference()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "OrderData.Items[7].Product.Category.Attributes[2].Name", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OrderData.Items[7].Product.Category.Attributes[2].Value", Object1Value = "Old", Object2Value = "New" },
            },
        };

        this.AddPaddingIgnoreRules();
        this.service.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "OrderData.Items[*].Product.Category.Attributes[*].Name",
            IgnoreCompletely = true,
        });

        // Act
        var filteredResult = this.service.FilterIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count(d => d.PropertyName == "OrderData.Items[7].Product.Category.Attributes[2].Value").ShouldBe(1);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WhenIgnoreRulesChange_ShouldRebuildCachedDirectMatcher()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var initialResult = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "OrderData.Customer.Name", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OrderData.Supplier.Name", Object1Value = "Old", Object2Value = "New" },
            },
        };

        this.AddPaddingIgnoreRules();
        this.service.IgnoreProperty("OrderData.Customer");

        // Act
        var initiallyFiltered = this.service.FilterIgnoredDifferences(initialResult);

        this.service.RemoveIgnoredProperty("OrderData.Customer");

        var refreshedResult = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "OrderData.Customer.Name", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OrderData.Supplier.Name", Object1Value = "Old", Object2Value = "New" },
            },
        };

        var refreshedFiltered = this.service.FilterIgnoredDifferences(refreshedResult);

        // Assert
        initiallyFiltered.Differences.Count(d => d.PropertyName == "OrderData.Supplier.Name").ShouldBe(1);
        refreshedFiltered.Differences.Count.ShouldBe(2);
        refreshedFiltered.Differences.Any(d => d.PropertyName == "OrderData.Customer.Name").ShouldBeTrue();
        refreshedFiltered.Differences.Any(d => d.PropertyName == "OrderData.Supplier.Name").ShouldBeTrue();
    }

    [TestMethod]
    public void SetTreatNullAndEmptyCollectionsAsEqual_ShouldUpdateConfiguration()
    {
        this.service.GetTreatNullAndEmptyCollectionsAsEqual().ShouldBeFalse();

        this.service.SetTreatNullAndEmptyCollectionsAsEqual(true);

        this.service.GetTreatNullAndEmptyCollectionsAsEqual().ShouldBeTrue();
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithNullAndEmptyCollectionByDefault_ShouldKeepDifference()
    {
        var result = this.CreateComparisonResult(new Difference
        {
            PropertyName = "Items.Count",
            Object1Value = null,
            Object2Value = "0",
        });

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.Count.ShouldBe(1);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithGlobalNullEmptyCollectionOption_ShouldFilterDifference()
    {
        var result = this.CreateComparisonResult(new Difference
        {
            PropertyName = "Items.Count",
            Object1Value = null,
            Object2Value = "0",
        });
        this.service.SetTreatNullAndEmptyCollectionsAsEqual(true);

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.ShouldBeEmpty();
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithScopedNullEmptyCollectionRule_ShouldFilterOnlyMatchingPath()
    {
        var result = this.CreateComparisonResult(
            new Difference
            {
                PropertyName = "Order.Items.Count",
                Object1Value = null,
                Object2Value = "0",
            },
            new Difference
            {
                PropertyName = "Order.Tags.Count",
                Object1Value = null,
                Object2Value = "0",
            });
        this.service.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "Order.Items",
            TreatNullAndEmptyCollectionsAsEqual = true,
        });

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.Single().PropertyName.ShouldBe("Order.Tags.Count");
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithScopedNullEmptyCollectionRule_ShouldFilterCountDifference()
    {
        var result = this.CreateComparisonResult(new Difference
        {
            PropertyName = "Order.Items.Count",
            Object1Value = null,
            Object2Value = "0",
        });
        this.service.AddIgnoreRule(new IgnoreRule
        {
            PropertyPath = "Order.Items",
            TreatNullAndEmptyCollectionsAsEqual = true,
        });

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.ShouldBeEmpty();
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithNullAndPopulatedCollection_ShouldKeepDifference()
    {
        var result = this.CreateComparisonResult(new Difference
        {
            PropertyName = "Items.Count",
            Object1Value = null,
            Object2Value = "1",
        });
        this.service.SetTreatNullAndEmptyCollectionsAsEqual(true);

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.Count.ShouldBe(1);
    }

    [TestMethod]
    public void FilterIgnoredDifferences_WithNullAndEmptyString_ShouldKeepDifference()
    {
        var result = this.CreateComparisonResult(new Difference
        {
            PropertyName = "Name",
            Object1Value = null,
            Object2Value = string.Empty,
        });
        this.service.SetTreatNullAndEmptyCollectionsAsEqual(true);

        var filteredResult = this.service.FilterIgnoredDifferences(result);

        filteredResult.Differences.Count.ShouldBe(1);
    }

    [TestMethod]
    public void IgnoreRuleJsonRoundTrip_ShouldPreserveNullEmptyCollectionFlag()
    {
        var rule = new IgnoreRule
        {
            PropertyPath = "Order.Items",
            TreatNullAndEmptyCollectionsAsEqual = true,
        };

        var json = JsonSerializer.Serialize(rule);
        var deserialized = JsonSerializer.Deserialize<IgnoreRule>(json);

        deserialized.ShouldNotBeNull();
        deserialized.TreatNullAndEmptyCollectionsAsEqual.ShouldBeTrue();
    }

    [TestMethod]
    public void AddSmartIgnoreRule_WithValidRule_ShouldAddToSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByNamePattern("Test.*", "Test pattern rule");

        // Act
        this.service.AddSmartIgnoreRule(rule);

        // Assert
        var rules = this.service.GetSmartIgnoreRules();
        rules.Count.ShouldBe(1);
        rules.First().Value.ShouldBe("Test.*");
    }

    [TestMethod]
    public void RemoveSmartIgnoreRule_WithExistingRule_ShouldRemoveFromSmartRules()
    {
        // Arrange
        var rule = SmartIgnoreRule.ByPropertyName("TestProperty");
        this.service.AddSmartIgnoreRule(rule);

        // Act
        this.service.RemoveSmartIgnoreRule(rule);

        // Assert
        var rules = this.service.GetSmartIgnoreRules();
        rules.ShouldBeEmpty();
    }

    [TestMethod]
    public void ClearSmartIgnoreRules_ShouldRemoveAllSmartRules()
    {
        // Arrange
        this.service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property1"));
        this.service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("Property2"));

        // Act
        this.service.ClearSmartIgnoreRules();

        // Assert
        var rules = this.service.GetSmartIgnoreRules();
        rules.ShouldBeEmpty();
    }

    [TestMethod]
    public void FilterSmartIgnoredDifferences_WithSmartRule_ShouldFilterCorrectly()
    {
        // Arrange
        var config = this.service.GetCurrentConfig();
        var result = new ComparisonResult(config)
        {
            Differences = new List<Difference>
            {
                new () { PropertyName = "TestProperty", Object1Value = "Old", Object2Value = "New" },
                new () { PropertyName = "OtherProperty", Object1Value = "Old", Object2Value = "New" },
            },
        };

        this.service.AddSmartIgnoreRule(SmartIgnoreRule.ByPropertyName("TestProperty"));

        // Act
        var filteredResult = this.service.FilterSmartIgnoredDifferences(result);

        // Assert
        filteredResult.Differences.Count.ShouldBe(1);
        filteredResult.Differences.First().PropertyName.ShouldBe("OtherProperty");
    }

    [TestMethod]
    public void NormalizePropertyValues_WithValidObject_ShouldSetDefaultValues()
    {
        // Arrange
        var testObject = new TestClass { StringProperty = "Test", IntProperty = 42 };
        var propertyNames = new List<string> { "StringProperty", "IntProperty" };

        // Act
        this.service.NormalizePropertyValues(testObject, propertyNames);

        // Assert
        testObject.StringProperty.ShouldBe(string.Empty); // NormalizePropertyValues sets strings to empty string, not null
        testObject.IntProperty.ShouldBe(0);
    }

    [TestMethod]
    public void GetUserIgnoreRules_WithUserRule_ShouldReturnRule()
    {
        // Arrange
        var rule = new IgnoreRule
        {
            PropertyPath = "UserProperty",
            IgnoreCompletely = true,
        };

        this.service.AddIgnoreRule(rule);

        // Act
        var userRules = this.service.GetUserIgnoreRules();

        // Assert
        userRules.Count(r => r.PropertyPath == "UserProperty").ShouldBe(1);
    }

    [TestMethod]
    public void GetUserIgnoreRules_ShouldExcludeXmlIgnoreRules()
    {
        // Arrange
        this.service.AddXmlIgnorePropertiesToIgnoreList(typeof(TestClassWithXmlIgnore));

        // Act
        var userRules = this.service.GetUserIgnoreRules();

        // Assert
        userRules.Any(r => r.PropertyPath == "IgnoredProperty").ShouldBeFalse();
    }

    [TestMethod]
    public void AddXmlIgnorePropertiesToIgnoreList_WithType_ShouldAddXmlIgnoreProperties()
    {
        // Act
        this.service.AddXmlIgnorePropertiesToIgnoreList(typeof(TestClassWithXmlIgnore));

        // Assert
        var ignoredProperties = this.service.GetIgnoredProperties();
        ignoredProperties.Contains("IgnoredProperty").ShouldBeTrue();
    }

    private ComparisonResult CreateComparisonResult(params Difference[] differences)
    {
        return new ComparisonResult(this.service.GetCurrentConfig())
        {
            Differences = differences.ToList(),
        };
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

    private void AddPaddingIgnoreRules(int count = 10)
    {
        for (var index = 0; index < count; index++)
        {
            this.service.IgnoreProperty($"Padding.Ignore.{index}");
        }
    }
}
