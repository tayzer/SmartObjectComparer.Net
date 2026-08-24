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
        using PluginTestPackage package = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");
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

        // Left unpinned on purpose: the profile follows the highest installed version,
        // so upgrading the plugin package does not strand it on a version that is gone.
        Assert.IsNull(profile.PluginVersion);

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
        private IReadOnlyList<InstalledPluginMetadata> plugins;

        public StubPluginMetadataProvider(params InstalledPluginMetadata[] plugins)
        {
            this.plugins = plugins;
        }

        public StubPluginMetadataProvider(string packageDirectory, string templateRequestDirectory)
        {
            plugins = new[] { new InstalledPluginMetadata(
                "acme.lookup",
                "1.0.0",
                "Acme",
                null,
                null,
                new[] { new PluginComparisonMetadata("acme.cmp", "Acme", Array.Empty<string>(), Array.Empty<string>()) },
                Array.Empty<PluginSdk.Configuration.PluginConfigurationSchema>(),
                new[] { new PluginSdk.Profiles.PluginEnvironment("QA", new Uri("https://qa/a"), new Uri("https://qa/b")) },
                new[] { new PluginSdk.Profiles.PluginProfileTemplate("acme-qa", "Acme QA", "acme.cmp", environmentName: "QA", requestDirectory: templateRequestDirectory) },
                packageDirectory) };
        }

        public void SetCatalog(params InstalledPluginMetadata[] plugins) => this.plugins = plugins;

        public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginCatalogView(plugins, Array.Empty<PluginInstallationFailure>()));

        public Task<PluginCatalogView> RefreshCatalogAsync(CancellationToken cancellationToken = default) =>
            GetCatalogAsync(cancellationToken);

        public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstalledPluginMetadata?>(plugins.FirstOrDefault());

        public Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<PluginComparisonDefinitionInfo?>(null);
    }

    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_DoesNotOverwriteAnEditedProfile()
    {
        using PluginTestPackage package = PluginTestPackage.WithFixture("parity.test-plugin", "1.0.0");
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

    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_RefreshesAnUneditedProfileWhenAHigherPluginVersionIsInstalled()
    {
        InstalledPluginMetadata versionOne = CreatePluginMetadata("1.0.0", "https://v1/a", isActive: true);
        StubPluginMetadataProvider metadata = new StubPluginMetadataProvider(versionOne);
        InMemoryRunProfileStore store = new InMemoryRunProfileStore();
        PluginProfileBootstrapper bootstrapper = new PluginProfileBootstrapper(metadata, store);

        await bootstrapper.EnsureTemplateProfilesAsync();

        metadata.SetCatalog(
            versionOne with { IsActive = false },
            CreatePluginMetadata("2.0.0", "https://v2/a", isActive: true));

        IReadOnlyList<string> materialised = await bootstrapper.EnsureTemplateProfilesAsync();

        CollectionAssert.AreEqual(new[] { "acme-qa" }, materialised.ToArray());
        Assert.AreEqual(new Uri("https://v2/a"), (await store.GetAsync("acme-qa"))!.EndpointA);
    }

    [TestMethod]
    public async Task EnsureTemplateProfilesAsync_DoesNotRefreshAProfileEditedAfterItsTemplateWasMaterialised()
    {
        InstalledPluginMetadata versionOne = CreatePluginMetadata("1.0.0", "https://v1/a", isActive: true);
        StubPluginMetadataProvider metadata = new StubPluginMetadataProvider(versionOne);
        InMemoryRunProfileStore store = new InMemoryRunProfileStore();
        PluginProfileBootstrapper bootstrapper = new PluginProfileBootstrapper(metadata, store);

        await bootstrapper.EnsureTemplateProfilesAsync();
        await store.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.cmp",
            new Uri("https://operator/a"),
            new Uri("https://service/b"),
            environmentName: "QA"));

        metadata.SetCatalog(
            versionOne with { IsActive = false },
            CreatePluginMetadata("2.0.0", "https://v2/a", isActive: true));

        IReadOnlyList<string> materialised = await bootstrapper.EnsureTemplateProfilesAsync();

        Assert.AreEqual(0, materialised.Count);
        Assert.AreEqual(new Uri("https://operator/a"), (await store.GetAsync("acme-qa"))!.EndpointA);
    }

    private static InstalledPluginMetadata CreatePluginMetadata(string version, string endpointA, bool isActive) =>
        new(
            "acme.lookup",
            version,
            "Acme",
            null,
            null,
            new[] { new PluginComparisonMetadata("acme.cmp", "Acme", Array.Empty<string>(), Array.Empty<string>()) },
            Array.Empty<PluginSdk.Configuration.PluginConfigurationSchema>(),
            new[] { new PluginSdk.Profiles.PluginEnvironment("QA", new Uri(endpointA), new Uri("https://service/b")) },
            new[] { new PluginSdk.Profiles.PluginProfileTemplate("acme-qa", "Acme QA", "acme.cmp", environmentName: "QA") },
            IsActive: isActive);

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
