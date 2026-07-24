using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Plugins;

namespace ParityBench.NET.Plugins.Tests;

/// <summary>
/// The bootstrapper is what makes a freshly installed plugin runnable with no manual
/// setup: its profile templates become saved run profiles.
/// </summary>
[TestClass]
public sealed class PluginProfileBootstrapperTests
{
    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_CreatesAProfileFromEachTemplateOnce()
    {
        using PluginTestPackage package = PluginTestPackage.InstallFixture("parity.test-plugin", "1.0.0");
        InMemoryRunProfileStore store = new InMemoryRunProfileStore();
        PluginProfileBootstrapper bootstrapper = new PluginProfileBootstrapper(
            new PluginMetadataProvider(new PluginCatalog(new[] { package.RootPath }), new PluginLoader()),
            store);

        IReadOnlyList<string> created = await bootstrapper.EnsureTemplateProfilesAsync();

        // The fixture ships one template ("test-template") with a "QA" environment.
        CollectionAssert.AreEqual(new[] { "test-template" }, created.ToArray());
        RunProfile profile = (await store.ListAsync()).Single();
        Assert.AreEqual("test-template", profile.Id);
        Assert.AreEqual("parity.test-plugin", profile.PluginId);
        Assert.AreEqual("parity.test-plugin.comparison", profile.ComparisonId);
        Assert.AreEqual("QA", profile.EnvironmentName);
        Assert.AreEqual(new Uri("https://qa.example.test/a"), profile.EndpointA);

        // Running it again is idempotent — the seeded profile is not recreated.
        IReadOnlyList<string> secondRun = await bootstrapper.EnsureTemplateProfilesAsync();
        Assert.AreEqual(0, secondRun.Count);
        Assert.AreEqual(1, (await store.ListAsync()).Count);
    }

    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_ResolvesAPackageRelativeRequestDirectoryAgainstThePackage()
    {
        InMemoryRunProfileStore store = new InMemoryRunProfileStore();
        // A plugin whose template ships a package-relative sample directory.
        StubPluginMetadataProvider metadata = new StubPluginMetadataProvider(
            packageDirectory: @"C:\plugins\acme",
            templateRequestDirectory: "samples");
        PluginProfileBootstrapper bootstrapper = new PluginProfileBootstrapper(metadata, store);

        await bootstrapper.EnsureTemplateProfilesAsync();

        RunProfile profile = (await store.ListAsync()).Single();
        Assert.AreEqual(Path.GetFullPath(@"C:\plugins\acme\samples"), profile.RequestDirectory);
    }

    private sealed class StubPluginMetadataProvider : IPluginMetadataProvider
    {
        private readonly InstalledPluginMetadata plugin;

        public StubPluginMetadataProvider(string packageDirectory, string templateRequestDirectory)
        {
            plugin = new InstalledPluginMetadata(
                "acme.lookup",
                "1.0.0",
                "Acme",
                null,
                null,
                new[] { new PluginComparisonMetadata("acme.cmp", "Acme", Array.Empty<string>(), Array.Empty<string>()) },
                Array.Empty<PluginSdk.Configuration.PluginConfigurationSchema>(),
                new[] { new PluginSdk.Profiles.PluginEnvironment("QA", new Uri("https://qa/a"), new Uri("https://qa/b")) },
                new[] { new PluginSdk.Profiles.PluginProfileTemplate("acme-qa", "Acme QA", "acme.cmp", environmentName: "QA", requestDirectory: templateRequestDirectory) },
                packageDirectory);
        }

        public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginCatalogView(new[] { plugin }, Array.Empty<PluginInstallationFailure>()));

        public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstalledPluginMetadata?>(plugin);
    }

    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_DoesNotOverwriteAnEditedProfile()
    {
        using PluginTestPackage package = PluginTestPackage.InstallFixture("parity.test-plugin", "1.0.0");
        InMemoryRunProfileStore store = new InMemoryRunProfileStore();
        // An operator already edited this profile's endpoints.
        await store.SaveAsync(new RunProfile(
            "test-template",
            "Edited",
            "parity.test-plugin",
            "parity.test-plugin.comparison",
            new Uri("https://edited/a"),
            new Uri("https://edited/b")));

        PluginProfileBootstrapper bootstrapper = new PluginProfileBootstrapper(
            new PluginMetadataProvider(new PluginCatalog(new[] { package.RootPath }), new PluginLoader()),
            store);

        IReadOnlyList<string> created = await bootstrapper.EnsureTemplateProfilesAsync();

        Assert.AreEqual(0, created.Count);
        Assert.AreEqual(new Uri("https://edited/a"), (await store.GetAsync("test-template"))!.EndpointA);
    }

    private sealed class InMemoryRunProfileStore : IRunProfileStore
    {
        private readonly Dictionary<string, RunProfile> profiles = new Dictionary<string, RunProfile>(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<RunProfile>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RunProfile>>(profiles.Values.ToArray());

        public Task<RunProfile?> GetAsync(string profileId, CancellationToken cancellationToken = default) =>
            Task.FromResult(profiles.TryGetValue(profileId, out RunProfile? profile) ? profile : null);

        public Task SaveAsync(RunProfile profile, CancellationToken cancellationToken = default)
        {
            profiles[profile.Id] = profile;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string profileId, CancellationToken cancellationToken = default)
        {
            profiles.Remove(profileId);
            return Task.CompletedTask;
        }
    }
}
