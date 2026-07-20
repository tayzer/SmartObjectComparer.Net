using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.UI.Workflow;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class RunWorkflowRuleParserTests
{
    [TestMethod]
    public void ParseIgnoreRules_WhenTextContainsPaths_ReturnsIgnoreRules()
    {
        IReadOnlyList<IgnoreRuleDefinition> rules = RunWorkflowRuleParser.ParseIgnoreRules(
            "# volatile fields\nReportId\nProviderTraceId");

        Assert.AreEqual(2, rules.Count);
        Assert.AreEqual("ReportId", rules[0].PropertyPath);
        Assert.AreEqual("ProviderTraceId", rules[1].PropertyPath);
    }

    [TestMethod]
    public void ParseSmartIgnoreRules_WhenKindValueLinesAreProvided_ReturnsSmartIgnoreRules()
    {
        IReadOnlyList<SmartIgnoreRuleDefinition> rules = RunWorkflowRuleParser.ParseSmartIgnoreRules(
            "PropertyName=ReportId\nNamePattern=.*TraceId$");

        Assert.AreEqual(2, rules.Count);
        Assert.AreEqual(SmartIgnoreRuleKind.PropertyName, rules[0].Kind);
        Assert.AreEqual("ReportId", rules[0].Value);
        Assert.AreEqual(SmartIgnoreRuleKind.NamePattern, rules[1].Kind);
        Assert.AreEqual(".*TraceId$", rules[1].Value);
    }

    [TestMethod]
    public void ParseMaskRules_WhenPipeOptionsAreProvided_ReturnsMaskRules()
    {
        IReadOnlyList<MaskRuleDefinition> rules = RunWorkflowRuleParser.ParseMaskRules(
            "Subject.NationalIdentifier|preserveLast=4|mask=#");

        Assert.AreEqual(1, rules.Count);
        Assert.AreEqual("Subject.NationalIdentifier", rules[0].PropertyPath);
        Assert.AreEqual(4, rules[0].PreserveLastCharacters);
        Assert.AreEqual("#", rules[0].MaskCharacter);
    }

    [TestMethod]
    public void ParseSmartIgnoreRules_WhenLineIsInvalid_ThrowsInvalidOperationException()
    {
        AssertThrows<InvalidOperationException>(() => RunWorkflowRuleParser.ParseSmartIgnoreRules("PropertyName"));
    }

    [TestMethod]
    public void ComparisonConfigurationFileSerializer_WhenV2ConfigurationProvided_RoundTripsSettingsAndRules()
    {
        ComparisonConfigurationFile configuration = new ComparisonConfigurationFile(
            1,
            new ComparisonConfigurationGlobalSettings(
                IgnoreCollectionOrder: true,
                IgnoreStringCase: true,
                IgnoreTrailingWhitespaceAtEnd: true,
                TreatNullAndEmptyCollectionsAsEqual: true,
                IgnoreXmlNamespaces: false),
            25,
            new[] { new IgnoreRuleDefinition("Subject.TraceId", ignoreCollectionOrder: true) });

        string json = ComparisonConfigurationFileSerializer.Serialize(configuration);
        ComparisonConfigurationFile parsed = ComparisonConfigurationFileSerializer.Deserialize(json);

        Assert.AreEqual(25, parsed.MaxDifferences);
        Assert.IsTrue(parsed.GlobalSettings.IgnoreCollectionOrder);
        Assert.IsTrue(parsed.GlobalSettings.IgnoreStringCase);
        Assert.AreEqual("Subject.TraceId", parsed.IgnoreRules.Single().PropertyPath);
        Assert.IsTrue(parsed.IgnoreRules.Single().IgnoreCollectionOrder);
    }

    [TestMethod]
    public void ComparisonConfigurationFileSerializer_WhenV1ConfigurationProvided_ImportsGlobalSettingsAndIgnoreRules()
    {
        string json = """
            {
              "schemaVersion": 1,
              "globalSettings": {
                "ignoreCollectionOrder": true,
                "ignoreStringCase": false,
                "ignoreTrailingWhitespaceAtEnd": true,
                "treatNullAndEmptyCollectionsAsEqual": true,
                "ignoreXmlNamespaces": true
              },
              "ignoreRules": [
                { "propertyPath": "SourceSystem", "ignoreCompletely": true }
              ]
            }
            """;

        ComparisonConfigurationFile parsed = ComparisonConfigurationFileSerializer.Deserialize(json);

        Assert.AreEqual(100, parsed.MaxDifferences);
        Assert.IsTrue(parsed.GlobalSettings.IgnoreCollectionOrder);
        Assert.IsTrue(parsed.GlobalSettings.IgnoreTrailingWhitespaceAtEnd);
        Assert.AreEqual("SourceSystem", parsed.IgnoreRules.Single().PropertyPath);
    }

    [TestMethod]
    public void MaskRuleFileSerializer_WhenArrayOrContainerProvided_ImportsRules()
    {
        IReadOnlyList<MaskRuleDefinition> arrayRules = MaskRuleFileSerializer.Deserialize(
            """[{"propertyPath":"Subject.Token","preserveLastCharacters":4,"maskCharacter":"#"}]""");
        IReadOnlyList<MaskRuleDefinition> containerRules = MaskRuleFileSerializer.Deserialize(
            """{"maskRules":[{"propertyPath":"Subject.Id","preserveLastCharacters":2,"maskCharacter":"*"}]}""");

        Assert.AreEqual("Subject.Token", arrayRules.Single().PropertyPath);
        Assert.AreEqual(4, arrayRules.Single().PreserveLastCharacters);
        Assert.AreEqual("#", arrayRules.Single().MaskCharacter);
        Assert.AreEqual("Subject.Id", containerRules.Single().PropertyPath);
        Assert.AreEqual(2, containerRules.Single().PreserveLastCharacters);
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

        Assert.Fail($"Expected {typeof(TException).Name}.");
    }
}
