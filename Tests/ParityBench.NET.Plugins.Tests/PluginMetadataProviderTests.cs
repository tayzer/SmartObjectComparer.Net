using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Plugins;

using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>
/// The metadata provider is what feeds the host UI's plugin catalog and profile
/// panels, so it must surface a real package's comparisons, schemas, environments
/// and templates as neutral, CLR-type-free metadata.
/// </summary>
[TestClass]
public sealed class PluginMetadataProviderTests
{
    [TestMethod]
    public async Task GetCatalogAsync_ForInstalledPackage_ReturnsItsComparisonsSchemasEnvironmentsAndTemplates()
    {
        using PluginTestPackage package = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader(package.ShadowRootPath));

        PluginCatalogView catalog = await provider.GetCatalogAsync();

        // Reported first: a package that failed to load lands here, and its reason is
        // far more useful than "sequence contains no elements" from the line below.
        Assert.AreEqual(0, catalog.Failures.Count, string.Join("; ", catalog.Failures.Select(failure => failure.Reason)));

        InstalledPluginMetadata plugin = catalog.Plugins.Single();
        Assert.AreEqual("parity.test-plugin", plugin.PluginId);
        Assert.AreEqual("1.0.0", plugin.Version);
        Assert.AreEqual("parity.test-plugin.comparison", plugin.Comparisons.Single().ComparisonId);

        PluginConfigurationSchema schema = plugin.ConfigurationSchemas.Single();
        PluginConfigurationField apiKey = schema.Fields.Single(field => field.Key == "apiKey");
        Assert.AreEqual(PluginFieldKind.Secret, apiKey.Kind);
        Assert.IsTrue(apiKey.IsRequired);

        Assert.AreEqual("QA", plugin.Environments.Single().Name);
        Assert.AreEqual("test-template", plugin.ProfileTemplates.Single().TemplateId);
    }

    [TestMethod]
    public async Task GetCatalogAsync_WhenAPackageIsIncompatible_ReportsItAsAFailure()
    {
        using PluginTestPackage package = PluginTestPackage.WithFixture(
            "parity.test-plugin",
            "1.0.0",
            sdkVersion: 999);
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader(package.ShadowRootPath));

        PluginCatalogView catalog = await provider.GetCatalogAsync();

        Assert.AreEqual(0, catalog.Plugins.Count);
        StringAssert.Contains(catalog.Failures.Single().Reason, "SDK version");
    }

    [TestMethod]
    public async Task GetCatalogAsync_ForInstalledPackage_ReportsTheManifestSdkVersionAndPackageDirectory()
    {
        using PluginTestPackage package = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { package.RootPath });
        PluginMetadataProvider provider = new PluginMetadataProvider(catalog, new PluginLoader(package.ShadowRootPath));

        InstalledPluginMetadata plugin = (await provider.GetCatalogAsync()).Plugins.Single();

        Assert.AreEqual(PluginManifest.CurrentSdkVersion, plugin.SdkVersion);

        // The installed package, not the copy the assemblies were loaded from: saved
        // profiles resolve package-relative paths against this.
        Assert.AreEqual(catalog.Packages[0].DirectoryPath, plugin.PackageDirectory);
    }

    [TestMethod]
    public async Task GetCatalogAsync_WhenTwoVersionsAreInstalled_MarksOnlyTheHighestAsActive()
    {
        using PluginTestPackage package = PluginTestPackage.CreateEmptyRoot();
        package.InstallFixture("parity.test-plugin", "1.0.0");
        package.InstallFixture("parity.test-plugin", "2.0.0");

        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader(package.ShadowRootPath));

        PluginCatalogView catalog = await provider.GetCatalogAsync();

        Assert.IsFalse(catalog.Plugins.Single(plugin => plugin.Version == "1.0.0").IsActive);
        Assert.IsTrue(catalog.Plugins.Single(plugin => plugin.Version == "2.0.0").IsActive);
    }

    [TestMethod]
    public async Task RefreshCatalogAsync_WhenAPackageIsInstalledAfterTheFirstCall_IncludesIt()
    {
        using PluginTestPackage package = PluginTestPackage.CreateEmptyRoot();
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader(package.ShadowRootPath));

        Assert.AreEqual(0, (await provider.GetCatalogAsync()).Plugins.Count);

        package.InstallFixture("parity.test-plugin", "1.0.0");

        // Reading the catalog again reports the last scan; only a refresh goes back
        // to disk, which is what the UI's Refresh button now does.
        Assert.AreEqual(0, (await provider.GetCatalogAsync()).Plugins.Count);
        Assert.AreEqual(1, (await provider.RefreshCatalogAsync()).Plugins.Count);
    }

    [TestMethod]
    public async Task GetPluginAsync_WhenNotInstalled_ReturnsNull()
    {
        using PluginTestPackage package = PluginTestPackage.CreateEmptyRoot();
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader(package.ShadowRootPath));

        Assert.IsNull(await provider.GetPluginAsync("missing.plugin"));
    }
}
