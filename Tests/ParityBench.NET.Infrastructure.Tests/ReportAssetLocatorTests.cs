using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Infrastructure.Reports;

namespace ParityBench.NET.Infrastructure.Tests;

[TestClass]
public sealed class ReportAssetLocatorTests
{
    private readonly List<string> tempDirectories = new List<string>();

    [TestCleanup]
    public void Cleanup()
    {
        foreach (string tempDirectory in tempDirectories)
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void Resolve_WhenConfiguredReportAssetsExist_ReturnsConfiguredPath()
    {
        string assetsDirectory = CreateReportAssetsDirectory();
        ReportAssetLocator locator = new ReportAssetLocator();

        string resolvedDirectory = locator.Resolve(assetsDirectory);

        Assert.AreEqual(Path.GetFullPath(assetsDirectory), resolvedDirectory);
    }

    [TestMethod]
    public void Resolve_WhenAssetsAreMissing_ThrowsInvalidOperationException()
    {
        string missingDirectory = Path.Combine(CreateTempDirectory(), "missing-assets");
        ReportAssetLocator locator = new ReportAssetLocator();

        try
        {
            locator.Resolve(missingDirectory);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Assert.Fail("Expected exception of type InvalidOperationException.");
    }

    private string CreateReportAssetsDirectory()
    {
        string assetsDirectory = CreateTempDirectory();
        Directory.CreateDirectory(Path.Combine(assetsDirectory, "_framework"));
        File.WriteAllText(Path.Combine(assetsDirectory, "index.html"), "<!doctype html><html></html>");
        return assetsDirectory;
    }

    private string CreateTempDirectory()
    {
        string tempDirectory = Path.Combine(Path.GetTempPath(), "ParityBenchReportAssetTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        tempDirectories.Add(tempDirectory);
        return tempDirectory;
    }
}