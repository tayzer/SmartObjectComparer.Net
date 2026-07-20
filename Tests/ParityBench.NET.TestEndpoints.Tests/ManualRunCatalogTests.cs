using System.Text.Json;
using System.Xml.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.ManualRunFixtureGenerator;

namespace ParityBench.NET.TestEndpoints.Tests;

[TestClass]
public sealed class ManualRunCatalogTests
{
    [ClassInitialize]
    public static void ClassInitialize(TestContext context)
    {
        string outputDirectory = Path.Combine(
            GetRepositoryRoot(),
            "Examples",
            "ParityBench.NET.ManualRuns",
            "client-soap-json-token",
            "volume");

        IReadOnlyList<ClientCustomerLookupFixtureGenerator.GeneratedFixture> fixtures =
            ClientCustomerLookupFixtureGenerator.Generate(count: 1000, startId: 10000);
        ClientCustomerLookupFixtureGenerator.WriteToDirectory(outputDirectory, fixtures);
    }

    [TestMethod]
    public async Task ManualRunCatalog_WhenLoaded_ReferencesExistingRequestDirectories()
    {
        string repositoryRoot = GetRepositoryRoot();
        string catalogPath = Path.Combine(repositoryRoot, "Examples", "ParityBench.NET.ManualRuns", "manual-run-catalog.json");

        await using FileStream stream = File.OpenRead(catalogPath);
        ManualRunCatalog catalog = await JsonSerializer.DeserializeAsync<ManualRunCatalog>(stream, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("Manual run catalog could not be read.");

        Assert.AreEqual(1, catalog.SchemaVersion);
        Assert.IsTrue(catalog.Runs.Count >= 5);

        foreach (ManualRunCatalogEntry run in catalog.Runs)
        {
            string requestDirectory = Path.Combine(repositoryRoot, run.RequestDirectory.Replace('/', Path.DirectorySeparatorChar));
            Assert.IsTrue(Directory.Exists(requestDirectory), $"Request directory was not found: {requestDirectory}");
            Assert.IsTrue(Directory.EnumerateFiles(requestDirectory).Any(), $"Request directory has no files: {requestDirectory}");
            StringAssert.Contains(run.EndpointA, "{baseUrl}");
            StringAssert.Contains(run.EndpointB, "{baseUrl}");
        }
    }

    [TestMethod]
    public void ProjectBoundary_WhenProjectIsTestEndpoints_ReferencesNoV1Projects()
    {
        string projectPath = Path.Combine(
            GetRepositoryRoot(),
            "Source",
            "ParityBench.NET.TestEndpoints",
            "ParityBench.NET.TestEndpoints.csproj");

        string[] projectReferences = XDocument
            .Load(projectPath)
            .Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value ?? string.Empty)
            .ToArray();

        string[] v1References = projectReferences
            .Where(reference => reference.Contains("ComparisonTool.", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        CollectionAssert.AreEqual(Array.Empty<string>(), v1References);
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

        throw new DirectoryNotFoundException("Could not locate ComparisonTool.sln.");
    }

    private sealed class ManualRunCatalog
    {
        public int SchemaVersion { get; set; }

        public List<ManualRunCatalogEntry> Runs { get; set; } = new List<ManualRunCatalogEntry>();
    }

    private sealed class ManualRunCatalogEntry
    {
        public string RequestDirectory { get; set; } = string.Empty;

        public string EndpointA { get; set; } = string.Empty;

        public string EndpointB { get; set; } = string.Empty;
    }
}
