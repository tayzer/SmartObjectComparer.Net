using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Plugins;

using ParityBench.PluginSdk.Configuration;

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
        using PluginTestPackage package = PluginTestPackage.InstallFixture("parity.test-plugin", "1.0.0");
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader());

        PluginCatalogView catalog = await provider.GetCatalogAsync();

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
        using PluginTestPackage package = PluginTestPackage.InstallFixture(
            "parity.test-plugin",
            "1.0.0",
            sdkVersion: 999);
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader());

        PluginCatalogView catalog = await provider.GetCatalogAsync();

        Assert.AreEqual(0, catalog.Plugins.Count);
        StringAssert.Contains(catalog.Failures.Single().Reason, "SDK version");
    }

    [TestMethod]
    public async Task GetPluginAsync_WhenNotInstalled_ReturnsNull()
    {
        using PluginTestPackage package = PluginTestPackage.CreateEmptyRoot();
        PluginMetadataProvider provider = new PluginMetadataProvider(
            new PluginCatalog(new[] { package.RootPath }),
            new PluginLoader());

        Assert.IsNull(await provider.GetPluginAsync("missing.plugin"));
    }
}
