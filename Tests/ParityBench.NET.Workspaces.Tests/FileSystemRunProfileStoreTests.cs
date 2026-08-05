using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Profiles;
using ParityBench.NET.Application.Secrets;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemRunProfileStoreTests
{
    [TestMethod]
    public async Task SaveAsync_ThenGetAsync_RoundTripsEveryProfileField()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        RunProfile profile = CreateProfile();

        await store.SaveAsync(profile);
        RunProfile? loaded = await store.GetAsync(profile.Id);

        Assert.IsNotNull(loaded);
        Assert.AreEqual(profile.Id, loaded.Id);
        Assert.AreEqual(profile.DisplayName, loaded.DisplayName);
        Assert.AreEqual(profile.PluginId, loaded.PluginId);
        Assert.AreEqual(profile.PluginVersion, loaded.PluginVersion);
        Assert.AreEqual(profile.ComparisonId, loaded.ComparisonId);
        Assert.AreEqual(profile.EnvironmentName, loaded.EnvironmentName);
        Assert.AreEqual(profile.EndpointA, loaded.EndpointA);
        Assert.AreEqual(profile.EndpointB, loaded.EndpointB);
        CollectionAssert.AreEqual(profile.EnabledStepIds.ToArray(), loaded.EnabledStepIds.ToArray());
        Assert.AreEqual("secret://acme/qa-key", loaded.StepConfiguration["acme.token"]["apiKey"]);
        Assert.AreEqual(profile.RequestDirectory, loaded.RequestDirectory);
        Assert.AreEqual(profile.RetentionModeOverride, loaded.RetentionModeOverride);
        Assert.IsTrue(loaded.Comparison.IgnoreCollectionOrder);
        Assert.AreEqual("trace.id", loaded.Comparison.IgnoreRules.Single().PropertyPath);
    }

    [TestMethod]
    public async Task SaveAsync_WhenProfileReferencesASecret_WritesOnlyTheReference()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);

        await store.SaveAsync(CreateProfile());

        string json = await File.ReadAllTextAsync(
            Path.Combine(workspace.Path, "config", "profiles", "client-lookup-qa.json"));
        StringAssert.Contains(json, "secret://acme/qa-key");
        // The value behind the reference must never reach disk.
        Assert.IsFalse(json.Contains("super-secret-value", StringComparison.Ordinal));
    }

    [TestMethod]
    public async Task ListAsync_WhenAProfileIsInvalid_SkipsItAndReturnsTheRest()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        await store.SaveAsync(CreateProfile());

        string profilesDirectory = Path.Combine(workspace.Path, "config", "profiles");
        await File.WriteAllTextAsync(Path.Combine(profilesDirectory, "broken.json"), "{ not json");

        IReadOnlyList<RunProfile> profiles = await store.ListAsync();

        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("client-lookup-qa", profiles[0].Id);
    }

    [TestMethod]
    public async Task GetAsync_WhenProfileIsSchema1_DropsTheAutomaticVersionPin()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        string profilesDirectory = Path.Combine(workspace.Path, "config", "profiles");
        Directory.CreateDirectory(profilesDirectory);

        // A profile written before the pin became a choice: seeding stamped the
        // installed version into it, which broke the profile once that version was
        // replaced by a newer package.
        await File.WriteAllTextAsync(
            Path.Combine(profilesDirectory, "legacy.json"),
            """
            {
              "schemaVersion": 1,
              "id": "legacy",
              "displayName": "Legacy",
              "plugin": { "id": "compare-a-b", "version": "1.0.0" },
              "comparisonId": "compare-a-b.default",
              "endpoints": { "a": "https://qa.example.test/a", "b": "https://qa.example.test/b" }
            }
            """);

        RunProfile? loaded = await store.GetAsync("legacy");

        Assert.IsNotNull(loaded);
        Assert.IsNull(loaded.PluginVersion);
        Assert.AreEqual(RunProfile.CurrentSchemaVersion, loaded.SchemaVersion);
    }

    [TestMethod]
    public async Task GetAsync_WhenProfileIsSchema2_KeepsAnExplicitVersionPin()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);

        await store.SaveAsync(CreateProfile());
        RunProfile? loaded = await store.GetAsync("client-lookup-qa");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("1.2.0", loaded.PluginVersion);
    }

    [TestMethod]
    public async Task DeleteAsync_WhenProfileExists_RemovesIt()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        await store.SaveAsync(CreateProfile());

        await store.DeleteAsync("client-lookup-qa");

        Assert.IsNull(await store.GetAsync("client-lookup-qa"));
        Assert.AreEqual(0, (await store.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ResolveAsync_WhenStepConfigurationReferencesASecret_SubstitutesTheStoredValue()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        await store.SaveAsync(CreateProfile());

        InMemorySecretStore secrets = new InMemorySecretStore();
        await secrets.SetAsync(new SecretReference("acme", "qa-key"), "super-secret-value");
        RunProfileResolver resolver = new RunProfileResolver(store, new SecretResolver(secrets));

        ResolvedRunProfile resolved = await resolver.ResolveAsync("client-lookup-qa");

        Assert.AreEqual("super-secret-value", resolved.Selection.StepConfiguration!["acme.token"]["apiKey"]);
        Assert.AreEqual("QA", resolved.Selection.EnvironmentName);
        Assert.AreEqual(new Uri("https://qa.example.test/soap"), resolved.EndpointA.Uri);
    }

    [TestMethod]
    public async Task ResolveAsync_WhenSecretIsMissing_ThrowsNamingTheEnvironmentVariableFallback()
    {
        using TempWorkspace workspace = TempWorkspace.Create();
        FileSystemRunProfileStore store = new FileSystemRunProfileStore(workspace.Path);
        await store.SaveAsync(CreateProfile());

        RunProfileResolver resolver = new RunProfileResolver(
            store,
            new SecretResolver(new ChainedSecretStore(new EnvironmentVariableSecretStore(), new InMemorySecretStore())));

        InvalidOperationException exception = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() =>
            resolver.ResolveAsync("client-lookup-qa"));

        StringAssert.Contains(exception.Message, "PB_SECRET_ACME_QA_KEY");
    }

    private static RunProfile CreateProfile() =>
        new RunProfile(
            "client-lookup-qa",
            "Client Customer Lookup — QA",
            "acme.customer-lookup",
            "acme.customer-lookup.soap-vs-json",
            new Uri("https://qa.example.test/soap"),
            new Uri("https://qa.example.test/json"),
            pluginVersion: "1.2.0",
            environmentName: "QA",
            enabledStepIds: new[] { "acme.token", "acme.enrich" },
            stepConfiguration: new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                ["acme.token"] = new Dictionary<string, string>
                {
                    ["apiKey"] = "secret://acme/qa-key",
                    ["tokenUrl"] = "https://qa.example.test/token",
                },
            },
            comparison: new ComparisonOptions(
                ignoreCollectionOrder: true,
                ignoreRules: new[] { new IgnoreRuleDefinition("trace.id") }),
            requestDirectory: @"C:\runs\client-lookup",
            retentionModeOverride: RetentionMode.None);

    private sealed class TempWorkspace : IDisposable
    {
        private TempWorkspace(string path) => Path = path;

        public string Path { get; }

        public static TempWorkspace Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "paritybench-profiles", Guid.NewGuid().ToString("n"));
            Directory.CreateDirectory(path);
            return new TempWorkspace(path);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }
}
