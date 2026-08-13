using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Secrets;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.UI.Plugins;
using ParityBench.NET.UI.Workflow;

using ParityBench.PluginSdk.Configuration;
using ParityBench.PluginSdk.Profiles;

namespace ParityBench.NET.UI.Tests;

[TestClass]
public sealed class PluginPanelsTests
{
    private BunitContext testContext = null!;
    private FakePluginMetadataProvider metadata = null!;
    private InMemoryRunProfileStore profileStore = null!;
    private InMemorySecretStore secretStore = null!;

    [TestInitialize]
    public void SetUp()
    {
        testContext = new BunitContext();
        testContext.JSInterop.Mode = JSRuntimeMode.Loose;
        testContext.Services.AddMudServices();
        testContext.RenderTree.Add<MudTestRoot>(parameters => { });

        metadata = new FakePluginMetadataProvider();
        profileStore = new InMemoryRunProfileStore();
        secretStore = new InMemorySecretStore();
        testContext.Services.AddSingleton<IPluginMetadataProvider>(metadata);
        testContext.Services.AddSingleton<IRunProfileStore>(profileStore);
        testContext.Services.AddSingleton<ISecretStore>(secretStore);
        testContext.Services.AddSingleton(new PluginProfileBootstrapper(metadata, profileStore));
    }

    [TestCleanup]
    public async Task TearDown() => await testContext.DisposeAsync().ConfigureAwait(false);

    [TestMethod]
    public void PluginCatalogPanel_ShowsInstalledPluginsComparisonsAndFailures()
    {
        IRenderedComponent<PluginCatalogPanel> component = testContext.Render<PluginCatalogPanel>();

        StringAssert.Contains(component.Markup, "Acme Lookup");
        StringAssert.Contains(component.Markup, "acme.lookup.soap-vs-json");
        StringAssert.Contains(component.Markup, "broken-package");
        StringAssert.Contains(component.Markup, "targets SDK version 999");
    }

    [TestMethod]
    public async Task PluginCatalogPanel_WhenRefreshIsClicked_RescansAndRendersTheNewPackage()
    {
        IRenderedComponent<PluginCatalogPanel> component = testContext.Render<PluginCatalogPanel>();
        Assert.IsFalse(component.Markup.Contains("Contoso Compare", StringComparison.Ordinal));

        // A plugin installed after the app started is only visible once disk is re-read.
        metadata.Plugins.Add(FakePluginMetadataProvider.CreatePlugin("1.0.0", "Contoso Compare", "contoso.compare"));
        await component.InvokeAsync(() => component.Find("button.pb-plugin-refresh").Click());

        Assert.AreEqual(1, metadata.RefreshCount);
        StringAssert.Contains(component.Markup, "Contoso Compare");
        StringAssert.Contains(component.Markup, "Last refreshed");
    }

    [TestMethod]
    public void PluginCatalogPanel_ShowsTheSdkVersionAndPackagePath()
    {
        IRenderedComponent<PluginCatalogPanel> component = testContext.Render<PluginCatalogPanel>();

        StringAssert.Contains(component.Markup, "SDK v1");
        StringAssert.Contains(component.Markup, @"C:\plugins\acme.lookup.1.0.0");
    }

    [TestMethod]
    public void PluginCatalogPanel_WhenTwoVersionsOfOnePluginAreInstalled_MarksOneActiveAndTheOtherSuperseded()
    {
        metadata.Plugins.Clear();
        metadata.Plugins.Add(FakePluginMetadataProvider.CreatePlugin("2.0.0", isActive: true));
        metadata.Plugins.Add(FakePluginMetadataProvider.CreatePlugin("1.0.0", isActive: false));

        IRenderedComponent<PluginCatalogPanel> component = testContext.Render<PluginCatalogPanel>();

        StringAssert.Contains(component.Markup, "Active");
        StringAssert.Contains(component.Markup, "Superseded");
    }

    [TestMethod]
    public void PluginCatalogPanel_WhenOnlyOneVersionIsInstalled_DoesNotLabelIt()
    {
        IRenderedComponent<PluginCatalogPanel> component = testContext.Render<PluginCatalogPanel>();

        // Saying "Active" when there is nothing to be active against is just noise.
        Assert.IsFalse(component.Markup.Contains("Superseded", StringComparison.Ordinal));
    }

    [TestMethod]
    public void RunProfilePanel_WhenTheCatalogVersionChanges_SeedsProfilesForNewlyDiscoveredPlugins()
    {
        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>(parameters => parameters
            .Add(p => p.CatalogVersion, 0));
        Assert.IsFalse(component.Markup.Contains("Acme QA 3.0.0", StringComparison.Ordinal));

        metadata.Plugins.Add(FakePluginMetadataProvider.CreatePlugin("3.0.0", "Contoso", "contoso.compare"));
        component.Render(parameters => parameters.Add(p => p.CatalogVersion, 1));

        // A plugin found by the refresh has never been seeded, so the invalidation has
        // to run the bootstrapper again rather than only reloading what already exists.
        StringAssert.Contains(component.Markup, "Acme QA 3.0.0");
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenTheCatalogVersionChanges_KeepsTheProfileBeingEdited()
    {
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json")));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>(parameters => parameters
            .Add(p => p.CatalogVersion, 0));
        component.Find("div.mud-list-item").Click();
        StringAssert.Contains(component.Markup, "https://qa/soap");

        component.Render(parameters => parameters.Add(p => p.CatalogVersion, 1));

        // A refresh must not throw away half-finished editing — the form may hold a
        // secret that has not reached the secret store yet.
        StringAssert.Contains(component.Markup, "https://qa/soap");
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenAProfilePinsAVersionThatIsGone_ShowsThePinAsNotInstalled()
    {
        // The shape of the upgrade failure: the profile names 0.9.0, only 1.0.0 is
        // installed. The pin has to stay visible and selected, or there is nothing
        // telling the operator why the run fails and no way off it.
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            pluginVersion: "0.9.0"));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();

        StringAssert.Contains(component.Markup, "0.9.0 (not installed)");
        StringAssert.Contains(component.Markup, "Pinned: this profile fails to run if the version is not installed.");
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenAProfilePinsTheInstalledVersion_MarksItAsTheLatest()
    {
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            pluginVersion: "1.0.0"));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();

        StringAssert.Contains(component.Markup, "1.0.0 (latest)");
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenSavingAnUnpinnedProfile_LeavesItUnpinned()
    {
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json")));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();
        StringAssert.Contains(component.Markup, "Latest installed (1.0.0)");

        await component.InvokeAsync(() => component.Find("button.mud-button-filled").Click());

        RunProfile? saved = await profileStore.GetAsync("acme-qa");
        Assert.IsNotNull(saved);
        // Saving must not quietly stamp the installed version back in — that is what
        // made every profile break on the next plugin upgrade.
        Assert.IsNull(saved.PluginVersion);
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenSavingAProfileWithARetentionMode_KeepsIt()
    {
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            retentionModeOverride: RetentionMode.None));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();
        StringAssert.Contains(component.Markup, "Keep everything");

        await component.InvokeAsync(() => component.Find("button.mud-button-filled").Click());

        RunProfile? saved = await profileStore.GetAsync("acme-qa");
        Assert.IsNotNull(saved);
        // Editing anything else must not silently drop the retention choice.
        Assert.AreEqual(RetentionMode.None, saved.RetentionModeOverride);
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenSavingCompareWorkers_PersistsExplicitValueAndOtherLargeRunOptions()
    {
        LargeRunOptions original = new(
            largeRunThreshold: 1500,
            chunkSize: 400,
            detailPageSize: 200,
            comparisonConcurrency: 12,
            progressUpdateItemInterval: 50,
            progressUpdateMillisecondsInterval: 250);
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            largeRun: original));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();
        var input = component.FindAll("input").Single(element => element.GetAttribute("value") == "12");
        input.Input("16");

        await component.InvokeAsync(() => component.Find("button.mud-button-filled").Click());

        RunProfile? saved = await profileStore.GetAsync("acme-qa");
        Assert.IsNotNull(saved);
        Assert.AreEqual(16, saved.LargeRun.ComparisonConcurrency);
        Assert.AreEqual(original.LargeRunThreshold, saved.LargeRun.LargeRunThreshold);
        Assert.AreEqual(original.ChunkSize, saved.LargeRun.ChunkSize);
        Assert.AreEqual(original.DetailPageSize, saved.LargeRun.DetailPageSize);
        Assert.AreEqual(original.ProgressUpdateItemInterval, saved.LargeRun.ProgressUpdateItemInterval);
        Assert.AreEqual(original.ProgressUpdateMillisecondsInterval, saved.LargeRun.ProgressUpdateMillisecondsInterval);
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenCompareWorkersIsBlank_SavesAutoAsNull()
    {
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            largeRun: new LargeRunOptions(comparisonConcurrency: 12)));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        component.Find("div.mud-list-item").Click();
        var input = component.FindAll("input").Single(element => element.GetAttribute("value") == "12");
        input.Input(string.Empty);

        await component.InvokeAsync(() => component.Find("button.mud-button-filled").Click());

        RunProfile? saved = await profileStore.GetAsync("acme-qa");
        Assert.IsNotNull(saved);
        Assert.IsNull(saved.LargeRun.ComparisonConcurrency);
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenAProfileIsSeededFromATemplate_ItIsNotPinnedToAVersion()
    {
        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>(parameters => parameters
            .Add(p => p.CatalogVersion, 0));

        RunProfile seeded = (await profileStore.ListAsync()).Single();
        Assert.IsNull(seeded.PluginVersion);
        StringAssert.Contains(component.Markup, "Acme QA 1.0.0");
    }

    [TestMethod]
    public void PluginConfigurationForm_RendersFieldsAndMasksSecrets()
    {
        Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        IRenderedComponent<PluginConfigurationForm> component = testContext.Render<PluginConfigurationForm>(parameters => parameters
            .Add(p => p.Schema, FakePluginMetadataProvider.Schema)
            .Add(p => p.Values, values));

        StringAssert.Contains(component.Markup, "Primary token URL");
        // Required fields are marked, and secret fields render as password inputs.
        StringAssert.Contains(component.Markup, "API key *");
        Assert.IsTrue(component.Markup.Contains("type=\"password\"", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task RunProfilePanel_WhenSavingASecretField_StoresAReferenceNotTheValue()
    {
        // Seed a profile whose secret step config holds a freshly entered value.
        await profileStore.SaveAsync(new RunProfile(
            "acme-qa",
            "Acme QA",
            "acme.lookup",
            "acme.lookup.soap-vs-json",
            new Uri("https://qa/soap"),
            new Uri("https://qa/json"),
            stepConfiguration: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["acme.request"] = new Dictionary<string, string> { ["apiKey"] = "super-secret" },
            }));

        IRenderedComponent<RunProfilePanel> component = testContext.Render<RunProfilePanel>();
        // Select the profile, then save it.
        component.Find("div.mud-list-item").Click();
        await component.InvokeAsync(() => component.Find("button.mud-button-filled").Click());

        RunProfile? saved = await profileStore.GetAsync("acme-qa");
        Assert.IsNotNull(saved);
        string persisted = saved.StepConfiguration["acme.request"]["apiKey"];
        Assert.IsTrue(persisted.StartsWith("secret://", StringComparison.Ordinal), persisted);
        Assert.IsFalse(persisted.Contains("super-secret", StringComparison.Ordinal));
        // The value went to the secret store under the reference the profile now holds.
        Assert.IsTrue(SecretReference.TryParse(persisted, out SecretReference? reference));
        Assert.AreEqual("super-secret", await secretStore.GetAsync(reference!));
    }

    private sealed class FakePluginMetadataProvider : IPluginMetadataProvider
    {
        public static readonly PluginConfigurationSchema Schema = new PluginConfigurationSchema(
            "acme.request",
            "Token exchange",
            new[]
            {
                new PluginConfigurationField("primaryTokenUrl", "Primary token URL", PluginFieldKind.Uri, isRequired: true),
                new PluginConfigurationField("apiKey", "API key", PluginFieldKind.Secret, isRequired: true),
            });

        // Mutable, so a test can change what disk "holds" between two calls — which is
        // the whole thing a refresh has to pick up.
        public List<InstalledPluginMetadata> Plugins { get; } = new List<InstalledPluginMetadata>
        {
            CreatePlugin("1.0.0", packageDirectory: @"C:\plugins\acme.lookup.1.0.0"),
        };

        public List<PluginInstallationFailure> Failures { get; } = new List<PluginInstallationFailure>
        {
            new PluginInstallationFailure("broken-package", "Plugin 'x' targets SDK version 999; this app supports 1."),
        };

        public int RefreshCount { get; private set; }

        public static InstalledPluginMetadata CreatePlugin(
            string version,
            string displayName = "Acme Lookup",
            string pluginId = "acme.lookup",
            string packageDirectory = "",
            bool isActive = true) =>
            new InstalledPluginMetadata(
                pluginId,
                version,
                displayName,
                "Reference",
                "Acme",
                new[] { new PluginComparisonMetadata("acme.lookup.soap-vs-json", "Acme SOAP vs JSON", new[] { "acme.request" }, new[] { "acme.request" }) },
                new[] { Schema },
                new[] { new PluginEnvironment("QA", new Uri("https://qa/soap"), new Uri("https://qa/json")) },
                new[] { new PluginProfileTemplate($"acme-qa-{version}", $"Acme QA {version}", "acme.lookup.soap-vs-json", environmentName: "QA") },
                packageDirectory,
                IsActive: isActive);

        public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginCatalogView(Plugins.ToArray(), Failures.ToArray()));

        public Task<PluginCatalogView> RefreshCatalogAsync(CancellationToken cancellationToken = default)
        {
            RefreshCount++;
            return GetCatalogAsync(cancellationToken);
        }

        public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(Plugins.FirstOrDefault(plugin =>
                string.Equals(plugin.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)
                && (version is null || string.Equals(plugin.Version, version, StringComparison.OrdinalIgnoreCase))));

        public Task<PluginComparisonDefinitionInfo?> ResolveComparisonDefinitionAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<PluginComparisonDefinitionInfo?>(null);
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
