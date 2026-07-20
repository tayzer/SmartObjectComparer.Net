using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ParityBench.NET.Fitness.Tests;

[TestClass]
[TestCategory("Fitness")]
public sealed class ArchitectureFitnessTests
{
    [TestMethod]
    public void ProjectReferences_WhenV2ProjectsAreLoaded_DoNotReferenceLegacyProjects()
    {
        string sourceRoot = Path.Combine(GetRepositoryRoot(), "Source");
        string[] projectPaths = Directory.GetFiles(sourceRoot, "ParityBench.NET.*.csproj", SearchOption.AllDirectories);

        foreach (string projectPath in projectPaths)
        {
            string[] references = LoadProjectReferences(projectPath);
            string[] legacyReferences = references
                .Where(reference => reference.Contains("ComparisonTool.", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            CollectionAssert.AreEqual(
                Array.Empty<string>(),
                legacyReferences,
                $"V2 project '{Path.GetFileNameWithoutExtension(projectPath)}' must not reference legacy ComparisonTool projects.");
        }
    }

    [TestMethod]
    public void ProjectReferences_WhenCoreV2LayersAreLoaded_MatchApprovedDependencyDirection()
    {
        AssertProjectReferences("ParityBench.NET.Domain", Array.Empty<string>());
        AssertProjectReferences("ParityBench.NET.Application", @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj");
        AssertProjectReferences(
            "ParityBench.NET.Engine",
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj");
        AssertProjectReferences(
            "ParityBench.NET.Infrastructure",
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj");
        AssertProjectReferences(
            "ParityBench.NET.Workspaces",
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj");
        AssertProjectReferences(
            "ParityBench.NET.UI",
            @"..\ParityBench.NET.Application\ParityBench.NET.Application.csproj",
            @"..\ParityBench.NET.Domain\ParityBench.NET.Domain.csproj");
    }

    [TestMethod]
    public void HostWiring_WhenV2HostsAreLoaded_BindsDefaultsAndObservability()
    {
        AssertSourceContains(
            Path.Combine("Source", "ParityBench.NET.Composition", "WorkspaceServiceCollectionExtensions.cs"),
            "services.AddParityBenchObservability(configuration",
            "services.Configure<RequestComparisonRunDefaults>(configuration.GetSection(\"RequestComparison:Defaults\"))");
        AssertSourceContains(
            Path.Combine("Source", "ParityBench.NET.Cli", "CliApplication.cs"),
            "services.AddParityBenchWorkspaceServices(");
        AssertSourceContains(
            Path.Combine("Source", "ParityBench.NET.Web", "Program.cs"),
            "services.AddParityBenchWorkspaceServices(");
        AssertSourceContains(
            Path.Combine("Source", "ParityBench.NET.Desktop", "App.xaml.cs"),
            "services.AddParityBenchWorkspaceServices(");
    }

    private static void AssertProjectReferences(string projectFolderName, params string[] expectedReferences)
    {
        string projectPath = Path.Combine(GetRepositoryRoot(), "Source", projectFolderName, $"{projectFolderName}.csproj");
        string[] actualReferences = LoadProjectReferences(projectPath);

        CollectionAssert.AreEquivalent(expectedReferences, actualReferences, $"Unexpected references for {projectFolderName}.");
    }

    private static string[] LoadProjectReferences(string projectPath)
    {
        XDocument project = XDocument.Load(projectPath);
        return project
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value.Replace('/', '\\') ?? string.Empty)
            .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AssertSourceContains(string relativePath, params string[] expectedFragments)
    {
        string source = File.ReadAllText(Path.Combine(GetRepositoryRoot(), relativePath));
        foreach (string fragment in expectedFragments)
        {
            StringAssert.Contains(source, fragment, $"Missing host wiring fragment '{fragment}' in {relativePath}.");
        }
    }

    private static string GetRepositoryRoot()
    {
        DirectoryInfo? currentDirectory = new DirectoryInfo(AppContext.BaseDirectory);
        while (currentDirectory is not null)
        {
            if (File.Exists(Path.Combine(currentDirectory.FullName, "ComparisonTool.sln")))
            {
                return currentDirectory.FullName;
            }

            currentDirectory = currentDirectory.Parent;
        }

        Assert.Fail("Could not find repository root containing ComparisonTool.sln.");
        throw new InvalidOperationException("Could not find repository root containing ComparisonTool.sln.");
    }
}
