using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Tests;

[TestClass]
public sealed class ComparisonOptionsTests
{
    [TestMethod]
    public void Create_WhenUsingDefaults_PreservesSliceTwoDefaults()
    {
        ComparisonOptions options = new ComparisonOptions();

        Assert.IsFalse(options.IgnoreCollectionOrder);
        Assert.IsFalse(options.IgnoreStringCase);
        Assert.IsFalse(options.IgnoreTrailingWhitespaceAtEnd);
        Assert.IsFalse(options.TreatNullAndEmptyCollectionsAsEqual);
        Assert.IsTrue(options.IgnoreXmlNamespaces);
        Assert.AreEqual(100, options.MaxDifferences);
        Assert.AreEqual(0, options.IgnoreRules.Count);
        Assert.AreEqual(0, options.SmartIgnoreRules.Count);
        Assert.AreEqual(0, options.MaskRules.Count);
    }

    [TestMethod]
    public void Create_WhenMaskRulePropertyPathIsEmpty_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => new MaskRuleDefinition(" "));
    }

    [TestMethod]
    public void Create_WhenIgnoreRulePropertyPathIsEmpty_ThrowsArgumentException()
    {
        AssertThrows<ArgumentException>(() => new IgnoreRuleDefinition(""));
    }

    [TestMethod]
    public void Create_WhenOptionsIncludeRules_StoresCopiedRuleSets()
    {
        IgnoreRuleDefinition[] ignoreRules = new[] { new IgnoreRuleDefinition("Name") };
        SmartIgnoreRuleDefinition[] smartIgnoreRules = new[] { new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Id") };
        MaskRuleDefinition[] maskRules = new[] { new MaskRuleDefinition("Token") };

        ComparisonOptions options = new ComparisonOptions(
            ignoreRules: ignoreRules,
            smartIgnoreRules: smartIgnoreRules,
            maskRules: maskRules);
        ignoreRules[0] = new IgnoreRuleDefinition("Changed");
        smartIgnoreRules[0] = new SmartIgnoreRuleDefinition(SmartIgnoreRuleKind.PropertyName, "Changed");
        maskRules[0] = new MaskRuleDefinition("Changed");

        Assert.AreEqual("Name", options.IgnoreRules[0].PropertyPath);
        Assert.AreEqual("Id", options.SmartIgnoreRules[0].Value);
        Assert.AreEqual("Token", options.MaskRules[0].PropertyPath);
    }

    [TestMethod]
    public void Create_WhenRunOptionsIncludeComparisonAndExecutionOptions_StoresOptions()
    {
        ComparisonOptions comparisonOptions = new ComparisonOptions(ignoreStringCase: true);
        RequestExecutionOptions requestExecutionOptions = new RequestExecutionOptions("application/xml");

        RunOptions runOptions = new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://service-a.example.test")),
            new EndpointDefinition(new Uri("https://service-b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            "Sample",
            comparisonOptions,
            requestExecutionOptions);

        Assert.AreSame(comparisonOptions, runOptions.Comparison);
        Assert.AreSame(requestExecutionOptions, runOptions.RequestExecution);
        Assert.AreEqual("application/xml", runOptions.RequestExecution.ContentTypeOverride);
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
        catch (Exception ex)
        {
            Assert.Fail($"Expected {typeof(TException).Name}, but got {ex.GetType().Name}.");
        }

        Assert.Fail($"Expected {typeof(TException).Name}, but no exception was thrown.");
    }
}
