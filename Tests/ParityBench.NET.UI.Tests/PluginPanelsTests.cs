using Bunit;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using MudBlazor.Services;

using ParityBench.NET.Application.Plugins;
using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Secrets;
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

        private static readonly InstalledPluginMetadata Plugin = new InstalledPluginMetadata(
            "acme.lookup",
            "1.0.0",
            "Acme Lookup",
            "Reference",
            "Acme",
            new[] { new PluginComparisonMetadata("acme.lookup.soap-vs-json", "Acme SOAP vs JSON", new[] { "acme.request" }, new[] { "acme.request" }) },
            new[] { Schema },
            new[] { new PluginEnvironment("QA", new Uri("https://qa/soap"), new Uri("https://qa/json")) },
            new[] { new PluginProfileTemplate("acme-qa", "Acme QA", "acme.lookup.soap-vs-json", environmentName: "QA") });

        public Task<PluginCatalogView> GetCatalogAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new PluginCatalogView(
                new[] { Plugin },
                new[] { new PluginInstallationFailure("broken-package", "Plugin 'x' targets SDK version 999; this app supports 1.") }));

        public Task<InstalledPluginMetadata?> GetPluginAsync(string pluginId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<InstalledPluginMetadata?>(string.Equals(pluginId, Plugin.PluginId, StringComparison.OrdinalIgnoreCase) ? Plugin : null);

        public Task<Type?> ResolveComparisonTypeAsync(string pluginId, string comparisonId, string? version = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<Type?>(null);
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
