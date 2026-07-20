using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class ProjectBoundaryTests
{
    [TestMethod]
    public void ProjectBoundary_WhenProjectIsWorkspaces_ReferencesOnlyApplicationAndDomain()
    {
        string[] expectedReferences = new[] { @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj", @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj" };

        AssertProjectReferences("ParityBench.NET.Workspaces", "ParityBench.NET.Workspaces.csproj", expectedReferences);
    }

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