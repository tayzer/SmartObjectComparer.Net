using System.Text.Json;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Plugins;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins.Tests;

[TestClass]
public sealed class PluginLoadingTests
{
    private static readonly string FixturePluginDirectory = ResolveFixturePluginDirectory();

    [TestMethod]
    public void Catalog_WhenPackageIsInstalled_DiscoversItFromTheManifestAlone()
    {
        using TempPluginRoot root = TempPluginRoot.Create();
        root.InstallFixture("parity.test-plugin", "1.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.Path });

        Assert.AreEqual(1, catalog.Packages.Count);
        Assert.AreEqual("parity.test-plugin", catalog.Packages[0].Manifest.Id);
        Assert.AreEqual(0, catalog.Failures.Count);
    }

    [TestMethod]
    public void Catalog_WhenPackageTargetsAnotherSdkVersion_ReportsItAsAFailureInsteadOfLoadingIt()
    {
        using TempPluginRoot root = TempPluginRoot.Create();
        root.InstallFixture("parity.test-plugin", "1.0.0", sdkVersion: PluginManifest.CurrentSdkVersion + 1);

        PluginCatalog catalog = new PluginCatalog(new[] { root.Path });

        Assert.AreEqual(0, catalog.Packages.Count);
        StringAssert.Contains(catalog.Failures.Single().Reason, "SDK version");
    }

    [TestMethod]
    public void Load_WhenTwoPackagesAreInstalled_IsolatesThemButSharesTheSdkTypes()
    {
        using TempPluginRoot root = TempPluginRoot.Create();
        // Same package installed at two versions: the loader must treat them as
        // separate plugins rather than unifying them.
        root.InstallFixture("parity.test-plugin", "1.0.0");
        root.InstallFixture("parity.test-plugin", "2.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.Path });
        PluginLoader loader = new PluginLoader();

        LoadedPlugin first = loader.Load(catalog.Packages.Single(package => package.Manifest.Version == "1.0.0"));
        LoadedPlugin second = loader.Load(catalog.Packages.Single(package => package.Manifest.Version == "2.0.0"));

        // Each package gets its own collectible context, so their own types differ...
        Assert.AreNotSame(first.LoadContext, second.LoadContext);
        Assert.AreNotSame(
            first.Registrations.Comparisons[0].GetType(),
            second.Registrations.Comparisons[0].GetType());

        // ...while the SDK contract types are shared with the host, which is what
        // lets the pipeline treat a plugin's middleware as IComparisonMiddleware.
        Assert.IsInstanceOfType<IComparisonDefinition>(first.Registrations.Comparisons[0]);
        Assert.IsInstanceOfType<IComparisonMiddleware>(second.Registrations.MiddlewareInstances[0]);
        Assert.AreSame(
            typeof(IComparisonMiddleware).Assembly,
            second.Registrations.MiddlewareInstances[0].GetType().GetInterfaces()
                .Single(contract => contract == typeof(IEndpointComparisonMiddleware)).Assembly);
    }

    [TestMethod]
    public async Task CreateAsync_WhenRunSelectsAComparison_BuildsAPlanWithTheEnabledSteps()
    {
        using TempPluginRoot root = TempPluginRoot.Create();
        root.InstallFixture("parity.test-plugin", "1.0.0");

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.Path }),
            new PluginLoader());

        await using ComparisonExecutionPlan? plan = await factory.CreateAsync(CreateRunOptions(
            new PluginComparisonSelection(
                "parity.test-plugin",
                "parity.test-plugin.comparison",
                stepConfiguration: new Dictionary<string, IReadOnlyDictionary<string, string>>
                {
                    ["parity.test-plugin.request"] = new Dictionary<string, string> { ["apiKey"] = "resolved-secret" },
                })));

        Assert.IsNotNull(plan);
        Assert.AreEqual("parity.test-plugin.comparison", plan.Definition.ComparisonId);
        Assert.AreEqual("parity.test-plugin.request", plan.PluginSteps.Single().StepId);
        Assert.AreEqual("resolved-secret", plan.Configuration.ForStep("parity.test-plugin.request").GetString("apiKey"));
    }

    [TestMethod]
    public async Task CreateAsync_WhenRunSelectsAnUninstalledPlugin_Throws()
    {
        using TempPluginRoot root = TempPluginRoot.Create();

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.Path }),
            new PluginLoader());

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(CreateRunOptions(new PluginComparisonSelection("missing.plugin", "missing.comparison"))));

        StringAssert.Contains(exception.Message, "is not installed");
    }

    [TestMethod]
    public async Task CreateAsync_WhenRunSelectsNoPlugin_ReturnsNull()
    {
        using TempPluginRoot root = TempPluginRoot.Create();

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.Path }),
            new PluginLoader());

        Assert.IsNull(await factory.CreateAsync(CreateRunOptions(null)));
    }

    private static RunOptions CreateRunOptions(PluginComparisonSelection? selection) =>
        new RunOptions(
            new RequestBatchReference("batch-1"),
            new EndpointDefinition(new Uri("https://a.example.test")),
            new EndpointDefinition(new Uri("https://b.example.test")),
            TimeSpan.FromSeconds(30),
            2,
            pluginComparison: selection);

    private static string ResolveFixturePluginDirectory()
    {
        // The fixture package is built alongside these tests; walk up to the repo
        // root rather than hard-coding a relative depth from the test binary.
        DirectoryInfo? directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ComparisonTool.sln")))
        {
            directory = directory.Parent;
        }

        string repositoryRoot = directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output directory.");

        string configuration = Path.GetFileName(Path.GetDirectoryName(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar)))!;
        return Path.Combine(repositoryRoot, "Tests", "Fixtures", "ParityBench.TestPlugin", "bin", configuration, "net10.0");
    }

    private sealed class TempPluginRoot : IDisposable
    {
        private TempPluginRoot(string path) => Path = path;

        public string Path { get; }

        public static TempPluginRoot Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paritybench-plugins", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);
            return new TempPluginRoot(path);
        }

        /// <summary>
        /// Copies the fixture package into the root under a given id and version, so
        /// two "different" plugins can be installed from one built fixture.
        /// </summary>
        public void InstallFixture(string pluginId, string version, int sdkVersion = PluginManifest.CurrentSdkVersion)
        {
            string packageDirectory = System.IO.Path.Combine(Path, $"{pluginId}.{version}");
            Directory.CreateDirectory(packageDirectory);

            foreach (string file in Directory.EnumerateFiles(FixturePluginDirectory))
            {
                File.Copy(file, System.IO.Path.Combine(packageDirectory, System.IO.Path.GetFileName(file)), overwrite: true);
            }

            PluginManifest manifest = new PluginManifest(
                pluginId,
                version,
                "ParityBench.TestPlugin.dll",
                sdkVersion,
                displayName: pluginId);
            File.WriteAllText(
                System.IO.Path.Combine(packageDirectory, PluginManifest.FileName),
                JsonSerializer.Serialize(manifest, PluginManifest.JsonOptions));
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Loaded plugin assemblies can keep files locked; a leftover temp
                // directory is not worth failing a test over.
            }
        }
    }
}
