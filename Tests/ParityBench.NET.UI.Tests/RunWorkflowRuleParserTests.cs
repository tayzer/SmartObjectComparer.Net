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
