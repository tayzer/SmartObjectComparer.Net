using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Plugins;

using ParityBench.PluginSdk.Comparisons;
using ParityBench.PluginSdk.Pipeline;
using ParityBench.PluginSdk.Plugin;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>
/// Covers picking up a plugin that a client installed, removed or rebuilt while the
/// app was already running.
/// </summary>
[TestClass]
public sealed class PluginReloadTests
{
    private const string PluginId = "parity.test-plugin";

    [TestMethod]
    public void Rescan_WhenAPackageIsAddedAfterConstruction_DiscoversIt()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        PluginCatalogSnapshot before = catalog.Current;

        root.InstallFixture(PluginId, "1.0.0");
        catalog.Rescan();

        Assert.AreEqual(1, catalog.Packages.Count);
        Assert.AreEqual(PluginId, catalog.Packages[0].Manifest.Id);

        // The earlier snapshot describes the moment it was taken and does not shift.
        Assert.AreEqual(0, before.Packages.Count);
    }

    [TestMethod]
    public void Rescan_WhenAPackageIsRemoved_DropsItFromThePackages()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        Assert.AreEqual(1, catalog.Packages.Count);

        root.UninstallPackage(PluginId, "1.0.0");
        catalog.Rescan();

        Assert.AreEqual(0, catalog.Packages.Count);
    }

    [TestMethod]
    public void Rescan_WhenAFailedPackageIsFixed_MovesItFromFailuresToPackages()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(
            PluginId,
            "1.0.0",
            sdkVersion: PluginManifest.CurrentSdkVersion + 1);

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        Assert.AreEqual(0, catalog.Packages.Count);
        Assert.AreEqual(1, catalog.Failures.Count);

        root.RewriteManifest(PluginId, "1.0.0", PluginManifest.CurrentSdkVersion);
        catalog.Rescan();

        Assert.AreEqual(1, catalog.Packages.Count);
        Assert.AreEqual(0, catalog.Failures.Count);
    }

    [TestMethod]
    public void Rescan_WhenNothingChanged_ProducesTheSameContentStamp()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        string first = catalog.Packages[0].ContentStamp;
        string second = catalog.Rescan().Packages[0].ContentStamp;

        // A stamp that moved on its own would evict and reload every plugin on every
        // refresh, which is the thing the shadow-copy leak is bounded against.
        Assert.AreEqual(first, second);
        Assert.AreNotEqual(string.Empty, first);
    }

    [TestMethod]
    public void Packages_WhenTheCallerMutatesTheReturnedList_Throws()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        // The snapshot hands out an array, so a caller cannot reach in and change what
        // a concurrent reader is looking at.
        Assert.ThrowsExactly<NotSupportedException>(() => ((IList<PluginPackage>)catalog.Packages).Add(null!));
    }

    [TestMethod]
    public void TryGet_WhenTwoVersionsAreInstalledAndNoVersionIsRequested_ReturnsTheHighest()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        // Installed lowest-first, which is also the order the file system enumerates
        // them, so picking the first would return 1.0.0.
        root.InstallFixture(PluginId, "1.0.0");
        root.InstallFixture(PluginId, "2.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        Assert.IsTrue(catalog.TryGet(PluginId, null, out PluginPackage? package));
        Assert.AreEqual("2.0.0", package!.Manifest.Version);
    }

    [TestMethod]
    public void TryGet_WhenVersionsDifferInNumericSegmentWidth_OrdersThemNumerically()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        root.InstallFixture(PluginId, "1.9.0");
        root.InstallFixture(PluginId, "1.10.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        Assert.IsTrue(catalog.TryGet(PluginId, null, out PluginPackage? package));
        Assert.AreEqual("1.10.0", package!.Manifest.Version);
    }

    [TestMethod]
    public void TryGet_WhenAVersionIsRequested_ReturnsThatVersionEvenWhenAHigherOneIsInstalled()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        root.InstallFixture(PluginId, "1.0.0");
        root.InstallFixture(PluginId, "2.0.0");

        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });

        Assert.IsTrue(catalog.TryGet(PluginId, "1.0.0", out PluginPackage? package));
        Assert.AreEqual("1.0.0", package!.Manifest.Version);
    }

    [TestMethod]
    public void IsActive_WhenTwoVersionsAreInstalled_IsTrueForTheHighestOnly()
    {
        using PluginTestPackage root = PluginTestPackage.CreateEmptyRoot();
        root.InstallFixture(PluginId, "1.0.0");
        root.InstallFixture(PluginId, "2.0.0");

        PluginCatalogSnapshot snapshot = new PluginCatalog(new[] { root.RootPath }).Current;

        Assert.IsFalse(snapshot.IsActive(snapshot.Packages.Single(package => package.Manifest.Version == "1.0.0")));
        Assert.IsTrue(snapshot.IsActive(snapshot.Packages.Single(package => package.Manifest.Version == "2.0.0")));
    }

    [TestMethod]
    public void Load_WhenTheSameVersionIsRebuilt_ReturnsANewlyLoadedPluginNotTheCachedOne()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        LoadedPlugin first = loader.Load(catalog.Packages[0]);
        Assert.AreEqual("Test comparison", first.Registrations.Comparisons[0].DisplayName);

        root.ReinstallFixture(PluginId, "1.0.0", FixtureVariant.Rebuilt);
        PluginPackage rebuilt = catalog.Rescan().Packages[0];

        // Assert the precondition separately, so a stamp regression reads as a stamp
        // failure rather than as a confusing behaviour failure below.
        Assert.AreNotEqual(first.Package.ContentStamp, rebuilt.ContentStamp);

        LoadedPlugin second = loader.Load(rebuilt);

        Assert.AreNotSame(first, second);
        Assert.AreNotSame(first.LoadContext, second.LoadContext);
        Assert.AreEqual("Test comparison (rebuilt)", second.Registrations.Comparisons[0].DisplayName);
    }

    [TestMethod]
    public void Load_WhenTheContentIsUnchanged_ReturnsTheCachedInstance()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        LoadedPlugin first = loader.Load(catalog.Packages[0]);
        LoadedPlugin second = loader.Load(catalog.Rescan().Packages[0]);

        // Refreshing without changing anything must not load a second copy, or the
        // load contexts would pile up once per refresh instead of once per build.
        Assert.AreSame(first, second);
    }

    [TestMethod]
    public void Load_WhenAPackageIsLoaded_DoesNotLockTheSourceAssembly()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        LoadedPlugin loaded = loader.Load(catalog.Packages[0]);

        // The whole point of shadow-copying: a client can overwrite or remove the
        // installed package while the app holds the plugin loaded.
        File.Delete(root.EntryAssemblyPath(PluginId, "1.0.0"));
        root.UninstallPackage(PluginId, "1.0.0");

        Assert.AreEqual(1, loaded.Registrations.Comparisons.Count);
    }

    [TestMethod]
    public void Load_WhenAPackageIsLoaded_CopiesTheWholePackageIntoTheShadowRoot()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        LoadedPlugin loaded = loader.Load(catalog.Packages[0]);

        Assert.AreNotEqual(catalog.Packages[0].DirectoryPath, loaded.ShadowDirectoryPath);
        Assert.IsTrue(File.Exists(Path.Combine(loaded.ShadowDirectoryPath, "ParityBench.TestPlugin.dll")));

        // The dependency resolver reads .deps.json from beside the entry assembly, so
        // a copy that dropped it would break any plugin with real dependencies.
        Assert.IsTrue(File.Exists(Path.Combine(loaded.ShadowDirectoryPath, "ParityBench.TestPlugin.deps.json")));
    }

    [TestMethod]
    public void Load_WhenTheSameVersionIsRebuilt_LeavesTheAlreadyResolvedRegistrationsUsable()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        using PluginLoader loader = new PluginLoader(root.ShadowRootPath);

        LoadedPlugin first = loader.Load(catalog.Packages[0]);
        IComparisonDefinition definition = first.Registrations.Comparisons[0];
        IComparisonMiddleware step = first.Registrations.MiddlewareInstances[0];

        root.ReinstallFixture(PluginId, "1.0.0", FixtureVariant.Rebuilt);
        loader.Load(catalog.Rescan().Packages[0]);

        // A run resolves its comparison and steps once and then holds them. Evicting
        // the old build must not disturb those, or a run in flight would break.
        Assert.AreEqual("parity.test-plugin.comparison", definition.ComparisonId);
        Assert.AreEqual("parity.test-plugin.request", step.StepId);
    }

    [TestMethod]
    public void Dispose_WhenPluginsAreStillLoaded_DoesNotThrow()
    {
        using PluginTestPackage root = PluginTestPackage.WithFixture(PluginId, "1.0.0");
        PluginCatalog catalog = new PluginCatalog(new[] { root.RootPath });
        PluginLoader loader = new PluginLoader(root.ShadowRootPath);
        loader.Load(catalog.Packages[0]);

        // Deleting a mapped copy fails, and cleanup is best-effort by design; asserting
        // the directory is gone would be flaky, so assert the contract that holds.
        loader.Dispose();
        loader.Dispose();
    }
}
