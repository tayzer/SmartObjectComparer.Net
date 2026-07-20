using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ComponentThreadingTests
{
    [TestMethod]
    public void Components_WhenAwaitingHostServices_DoNotUseConfigureAwaitFalse()
    {
        string uiRoot = Path.Combine(GetRepositoryRoot(), "Source", "ParityBench.NET.UI");
        string[] componentPaths = Directory.GetFiles(uiRoot, "*.razor", SearchOption.AllDirectories);

        foreach (string componentPath in componentPaths)
        {
            string content = File.ReadAllText(componentPath);

            Assert.IsFalse(
                content.Contains("ConfigureAwait(false)", StringComparison.Ordinal),
                $"Component '{componentPath}' should stay on the Blazor renderer dispatcher.");
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);

        while (currentDirectory is not null)
        {
            string solutionPath = Path.Combine(currentDirectory.FullName, "ComparisonTool.sln");
            if (File.Exists(solutionPath))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        Assert.Fail("Could not find repository root containing ComparisonTool.sln.");
        throw new InvalidOperationException("Could not find repository root containing ComparisonTool.sln.");
    }
}
