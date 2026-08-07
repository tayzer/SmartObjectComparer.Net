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
    [TestMethod]
    public void Catalog_WhenPackageIsInstalled_DiscoversItFromTheManifestAlone()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        Assert.AreEqual(1, catalog.Packages.Count);
        Assert.AreEqual("parity.test-plugin", catalog.Packages[0].Manifest.Id);
        Assert.AreEqual(0, catalog.Failures.Count);
    }

    [TestMethod]
    public void Catalog_WhenPackageTargetsAnotherSdkVersion_ReportsItAsAFailureInsteadOfLoadingIt()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(
            "parity.test-plugin",
            "1.0.0",
            sdkVersion: PluginManifest.CurrentSdkVersion + 1);

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        Assert.AreEqual(0, catalog.Packages.Count);
        StringAssert.Contains(catalog.Failures.Single().Reason, "SDK version");
    }

    [TestMethod]
    public void Load_WhenTwoPackagesAreInstalled_IsolatesThemButSharesTheSdkTypes()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        // Same package installed at two versions: the loader must treat them as
        // separate plugins rather than unifying them.
        root.InstallFixture("parity.test-plugin", "1.0.0");
        root.InstallFixture("parity.test-plugin", "2.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

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
        using PluginTestPackage root = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.RootPath }),
            loader);

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
    public async Task CreateAsync_WhenTwoVersionsAreInstalledAndTheProfileNamesNoVersion_UsesTheHighest()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        root.InstallFixture("parity.test-plugin", "1.0.0");
        root.InstallFixture("parity.test-plugin", "2.0.0", variant: FixtureVariant.Rebuilt);

        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);
        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.RootPath }),
            loader);

        await using ComparisonExecutionPlan? plan = await factory.CreateAsync(CreateRunOptions(
            new PluginComparisonSelection("parity.test-plugin", "parity.test-plugin.comparison")));

        Assert.IsNotNull(plan);

        // Only the 2.0.0 package carries the rebuilt build, so the display name proves
        // which of the two was resolved.
        Assert.AreEqual("Test comparison (rebuilt)", plan.Definition.DisplayName);
    }

    [TestMethod]
    public async Task CreateAsync_WhenRunSelectsAnUninstalledPlugin_Throws()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.RootPath }),
            loader);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(CreateRunOptions(new PluginComparisonSelection("missing.plugin", "missing.comparison"))));

        StringAssert.Contains(exception.Message, "is not installed");
    }

    [TestMethod]
    public async Task CreateAsync_WhenTheProfilePinsAVersionThatIsNotInstalled_NamesTheInstalledVersions()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture("parity.test-plugin", "2.0.0");
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.RootPath }),
            loader);

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            factory.CreateAsync(CreateRunOptions(new PluginComparisonSelection(
                "parity.test-plugin",
                "parity.test-plugin.comparison",
                pluginVersion: "1.0.0"))));

        StringAssert.Contains(exception.Message, "2.0.0");
    }

    [TestMethod]
    public async Task CreateAsync_WhenRunSelectsNoPlugin_ReturnsNull()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        PluginComparisonPlanFactory factory = new PluginComparisonPlanFactory(
            new PluginCatalog(new[] { root.RootPath }),
            loader);

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
}
