using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Cli;
using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Cli.Tests;

[TestClass]
public sealed class BaselineCommandTests
{
    [TestMethod]
    public void Parse_WhenCaptureBaselineIsProvidedWithARunProfile_ReturnsTheCaptureName()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "requests",
            "--run-profile",
            "client-lookup",
            "--capture-baseline",
            "Orders upgrade",
        });

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.AreEqual("Orders upgrade", result.Options!.CaptureBaselineName);
        Assert.IsNull(result.Options.BaselineReference);
    }

    [TestMethod]
    public void Parse_WhenBaselineIsProvidedWithoutDirectoryOrEndpoints_ReturnsOptions()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--run-profile",
            "client-lookup",
            "--baseline",
            "orders@3",
        });

        // Replay supplies the requests and the expected side, so neither a directory
        // nor endpoint A is required.
        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.AreEqual("orders@3", result.Options!.BaselineReference);
    }

    [TestMethod]
    public void Parse_WhenBaselineIsProvidedWithoutARunProfile_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "requests",
            "--endpoint-a",
            "https://a.example.test",
            "--endpoint-b",
            "https://b.example.test",
            "--baseline",
            "orders@3",
        });

        Assert.IsFalse(result.IsSuccess);
        Assert.IsTrue(result.Errors.Any(error => error.Contains("--run-profile", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void Parse_WhenBothCaptureAndReplayAreRequested_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--run-profile",
            "client-lookup",
            "--capture-baseline",
            "Orders upgrade",
            "--baseline",
            "orders@3",
        });

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void Parse_WhenTheBaselineVersionIsNotANumber_ReturnsValidationError()
    {
        RequestCommandParseResult result = RequestCommandParser.Parse(new[]
        {
            "request",
            "--run-profile",
            "client-lookup",
            "--baseline",
            "orders@latest",
        });

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void BaselineParse_WhenExportIsRequested_ReturnsIdAndVersion()
    {
        BaselineCommandParseResult result = BaselineCommandParser.Parse(new[]
        {
            "baseline",
            "export",
            "orders@3",
            "orders.pbbaseline",
        });

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.AreEqual(BaselineCommandAction.Export, result.Options!.Action);
        Assert.AreEqual(new BaselineId("orders"), result.Options.Id);
        Assert.AreEqual(3, result.Options.Version);
        Assert.AreEqual("orders.pbbaseline", result.Options.Path);
    }

    [TestMethod]
    public void BaselineParse_WhenDeleteOmitsTheVersion_TargetsEveryVersion()
    {
        BaselineCommandParseResult result = BaselineCommandParser.Parse(new[] { "baseline", "delete", "orders" });

        Assert.IsTrue(result.IsSuccess, string.Join("; ", result.Errors));
        Assert.AreEqual(BaselineCommandAction.Delete, result.Options!.Action);
        Assert.IsNull(result.Options.Version);
    }

    [TestMethod]
    public void BaselineParse_WhenTheActionIsUnknown_ReturnsValidationError()
    {
        BaselineCommandParseResult result = BaselineCommandParser.Parse(new[] { "baseline", "publish" });

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public void BaselineParse_WhenExportHasNoTargetPath_ReturnsValidationError()
    {
        BaselineCommandParseResult result = BaselineCommandParser.Parse(new[] { "baseline", "export", "orders@3" });

        Assert.IsFalse(result.IsSuccess);
    }
}
