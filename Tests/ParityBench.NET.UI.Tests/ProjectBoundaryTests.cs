using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class ProjectBoundaryTests
{
    [TestMethod]
    public void ProjectBoundary_WhenProjectIsUi_ReferencesOnlyApplicationAndDomain()
    {
        string[] expectedReferences = new[]
        {
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj",
        };

        AssertProjectReferences("ParityBench.NET.UI", "ParityBench.NET.UI.csproj", expectedReferences);
    }

    [TestMethod]
    public void ProjectBoundary_WhenProjectIsWeb_ReferencesOnlyV2Projects()
    {
        string[] expectedReferences = ExpectedHostReferences();

        AssertProjectReferences("ParityBench.NET.Web", "ParityBench.NET.Web.csproj", expectedReferences);
    }

    [TestMethod]
    public void ProjectBoundary_WhenProjectIsDesktop_ReferencesOnlyV2Projects()
    {
        string[] expectedReferences = ExpectedHostReferences();

        AssertProjectReferences("ParityBench.NET.Desktop", "ParityBench.NET.Desktop.csproj", expectedReferences);
    }

    [TestMethod]
    public void ProjectBoundary_WhenProjectIsReport_ReferencesOnlyDomainAndUi()
    {
        string[] expectedReferences = new[]
        {
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj",
            @"..\ParityBench.NET.UI\ParityBench.NET.UI.csproj",
        };

        AssertProjectReferences("ParityBench.NET.Report", "ParityBench.NET.Report.csproj", expectedReferences);
    }

    [TestMethod]
    public void ProjectBoundary_WhenProjectIsV2_DoesNotReferenceV1Projects()
    {
        string sourceRoot = Path.Combine(GetRepositoryRoot(), "Source");
        string[] projectPaths = Directory.GetFiles(sourceRoot, "ParityBench.NET.*.csproj", SearchOption.AllDirectories);

        foreach (string projectPath in projectPaths)
        {
            string[] references = LoadProjectReferences(projectPath);
            string[] v1References = references
                .Where(reference => reference.Contains("ComparisonTool.", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            CollectionAssert.AreEqual(Array.Empty<string>(), v1References, $"Project '{projectPath}' should not reference V1 projects.");
        }
    }

    private static string[] ExpectedHostReferences() =>
        new[]
        {
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj",
            @"..\ParityBench.NET.Engine\ParityBench.NET.Engine.csproj",
            @"..\ParityBench.NET.Infrastructure\ParityBench.NET.Infrastructure.csproj",
            @"..\ParityBench.NET.UI\ParityBench.NET.UI.csproj",
            @"..\ParityBench.NET.Workspaces\ParityBench.NET.Workspaces.csproj",
        };

    private static void AssertProjectReferences(
        string projectFolderName,
        string projectFileName,
        string[] expectedReferences)
    {
        string projectPath = Path.Combine(GetRepositoryRoot(), "Source", projectFolderName, projectFileName);
        string[] actualReferences = LoadProjectReferences(projectPath);

        CollectionAssert.AreEquivalent(expectedReferences, actualReferences);
    }

    private static string[] LoadProjectReferences(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);

        return project
            .Descendants("ProjectReference")
            .Select(GetReferenceInclude)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string GetReferenceInclude(XElement projectReference)
    {
        XAttribute? include = projectReference.Attribute("Include");
        if (include is null)
        {
            Assert.Fail("ProjectReference is missing Include.");
            throw new InvalidOperationException("ProjectReference is missing Include.");
        }

        return include.Value.Replace('/', '\\');
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