using System.IO.Compression;
using System.Text;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Application.Baselines;
using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemBaselineStoreTests
{
    private string workspaceRoot = string.Empty;
    private FileSystemBaselineStore store = null!;

    [TestInitialize]
    public void Initialize()
    {
        workspaceRoot = Path.Combine(Path.GetTempPath(), "paritybench-baseline-store-tests", Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(workspaceRoot);
        store = new FileSystemBaselineStore(workspaceRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        try
        {
            if (Directory.Exists(workspaceRoot))
            {
                Directory.Delete(workspaceRoot, recursive: true);
            }
        }
        catch (IOException)
        {
        }
    }

    [TestMethod]
    public async Task CompleteCaptureAsync_RoundTripsTheManifestAndItsScenarios()
    {
        BaselinePackageManifest reserved = await store.BeginCaptureAsync(CreateRequest());
        await store.AppendScenarioAsync(reserved.Id, reserved.Version, CreateScenario("orders/one.xml"));
        BaselinePackageManifest completed = await store.CompleteCaptureAsync(reserved.Id, reserved.Version);

        BaselinePackageManifest? loaded = await store.LoadManifestAsync(completed.Id, completed.Version);

        Assert.IsNotNull(loaded);
        Assert.AreEqual("Orders upgrade", loaded.Name);
        Assert.AreEqual(1, loaded.Version);
        Assert.AreEqual("run-1", loaded.CapturedFromRunId);
        Assert.AreEqual("client.lookup", loaded.PluginId);
        Assert.AreEqual("2.1.0", loaded.PluginVersion);
        Assert.AreEqual("staging", loaded.EnvironmentName);
        Assert.AreEqual(new Uri("https://legacy.example.test/lookup"), loaded.CaptureEndpoint);
        BaselineScenarioEntry scenario = loaded.Scenarios.Single();
        Assert.AreEqual("orders/one.xml", scenario.RelativePath);
        Assert.AreEqual(200, scenario.StatusCode);
        Assert.IsTrue(scenario.HasRawResponse);
        // The comparison settings in force at capture travel with the package, so a
        // later replay can show what the expected side was produced under.
        Assert.IsTrue(loaded.ComparisonOptions.IgnoreCollectionOrder);
    }

    [TestMethod]
    public async Task BeginCaptureAsync_WhenTheNameAlreadyExists_ReservesTheNextVersion()
    {
        await CaptureAsync("Orders upgrade");
        await CaptureAsync("Orders upgrade");

        IReadOnlyList<BaselineSummary> baselines = await store.ListAsync();

        // Same package, two versions: a completed version is never rewritten, so an
        // approved expected result stays exactly as it was captured.
        CollectionAssert.AreEquivalent(new[] { 1, 2 }, baselines.Select(baseline => baseline.Version).ToArray());
        Assert.AreEqual(1, baselines.Select(baseline => baseline.Id.Value).Distinct().Count());
    }

    [TestMethod]
    public async Task LoadManifestAsync_WithoutAVersion_ResolvesTheLatestCompletedOne()
    {
        await CaptureAsync("Orders upgrade");
        BaselinePackageManifest second = await CaptureAsync("Orders upgrade");

        BaselinePackageManifest? latest = await store.LoadManifestAsync(second.Id);

        Assert.IsNotNull(latest);
        Assert.AreEqual(2, latest.Version);
    }

    [TestMethod]
    public async Task ListAsync_IgnoresCapturesThatNeverCompleted()
    {
        BaselinePackageManifest reserved = await store.BeginCaptureAsync(CreateRequest());
        await store.AppendScenarioAsync(reserved.Id, reserved.Version, CreateScenario("orders/one.xml"));

        Assert.AreEqual(0, (await store.ListAsync()).Count);

        await store.CompleteCaptureAsync(reserved.Id, reserved.Version);

        Assert.AreEqual(1, (await store.ListAsync()).Count);
    }

    [TestMethod]
    public async Task AbandonCaptureAsync_LeavesCompletedVersionsAlone()
    {
        BaselinePackageManifest completed = await CaptureAsync("Orders upgrade");

        await store.AbandonCaptureAsync(completed.Id, completed.Version);

        Assert.AreEqual(1, (await store.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ExportRequestsToDirectoryAsync_WritesEveryStoredRequest()
    {
        BaselinePackageManifest reserved = await store.BeginCaptureAsync(CreateRequest());
        await store.AppendScenarioAsync(reserved.Id, reserved.Version, CreateScenario("orders/one.xml", "<request>1</request>"));
        await store.AppendScenarioAsync(reserved.Id, reserved.Version, CreateScenario("orders/two.xml", "<request>2</request>"));
        await store.CompleteCaptureAsync(reserved.Id, reserved.Version);

        string target = Path.Combine(workspaceRoot, "staged");
        int copied = await store.ExportRequestsToDirectoryAsync(reserved.Id, reserved.Version, target);

        Assert.AreEqual(2, copied);
        Assert.AreEqual("<request>1</request>", await File.ReadAllTextAsync(Path.Combine(target, "orders", "one.xml")));
        Assert.AreEqual("<request>2</request>", await File.ReadAllTextAsync(Path.Combine(target, "orders", "two.xml")));
    }

    [TestMethod]
    public async Task OpenCanonicalAsync_ReturnsTheStoredComparisonModel()
    {
        BaselinePackageManifest reserved = await store.BeginCaptureAsync(CreateRequest());
        await store.AppendScenarioAsync(
            reserved.Id,
            reserved.Version,
            CreateScenario("orders/one.xml", canonical: "{\"status\":\"OK\"}"));
        await store.CompleteCaptureAsync(reserved.Id, reserved.Version);

        await using Stream canonical = await store.OpenCanonicalAsync(reserved.Id, reserved.Version, "orders/one.xml");
        using StreamReader reader = new StreamReader(canonical);

        Assert.AreEqual("{\"status\":\"OK\"}", await reader.ReadToEndAsync());
    }

    [TestMethod]
    public async Task ImportAsync_RoundTripsAnExportedPackageAsANewVersion()
    {
        BaselinePackageManifest exported = await CaptureAsync("Orders upgrade");
        string archivePath = Path.Combine(workspaceRoot, "orders.pbbaseline");
        await store.ExportAsync(exported.Id, exported.Version, archivePath);
        await store.DeleteAsync(exported.Id);

        BaselinePackageManifest imported = await store.ImportAsync(archivePath);

        Assert.AreEqual(exported.Id, imported.Id);
        Assert.AreEqual(exported.Name, imported.Name);
        Assert.AreEqual(exported.CapturedAt, imported.CapturedAt);
        Assert.AreEqual(exported.Scenarios.Count, imported.Scenarios.Count);
        await using Stream canonical = await store.OpenCanonicalAsync(imported.Id, imported.Version, "orders/one.xml");
        Assert.IsTrue(canonical.Length > 0);
    }

    [TestMethod]
    public async Task ImportAsync_WhenTheNameAlreadyExists_AddsAVersionInsteadOfOverwriting()
    {
        BaselinePackageManifest exported = await CaptureAsync("Orders upgrade");
        string archivePath = Path.Combine(workspaceRoot, "orders.pbbaseline");
        await store.ExportAsync(exported.Id, exported.Version, archivePath);

        BaselinePackageManifest imported = await store.ImportAsync(archivePath);

        Assert.AreEqual(2, imported.Version);
        Assert.AreEqual(2, (await store.ListAsync()).Count);
    }

    [TestMethod]
    public async Task ImportAsync_WhenAnEntryEscapesThePackage_RefusesTheArchive()
    {
        string archivePath = Path.Combine(workspaceRoot, "malicious.pbbaseline");
        BaselinePackageManifest exported = await CaptureAsync("Orders upgrade");
        await store.ExportAsync(exported.Id, exported.Version, archivePath);

        // Append a traversal entry to an otherwise valid package.
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Update))
        {
            ZipArchiveEntry entry = archive.CreateEntry("../../escaped.txt");
            await using Stream entryStream = entry.Open();
            await entryStream.WriteAsync(Encoding.UTF8.GetBytes("escaped"));
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ImportAsync(archivePath));
        Assert.IsFalse(File.Exists(Path.Combine(workspaceRoot, "escaped.txt")));
    }

    [TestMethod]
    public async Task ImportAsync_WhenTheArchiveIsNotAPackage_Fails()
    {
        string archivePath = Path.Combine(workspaceRoot, "not-a-package.pbbaseline");
        using (ZipArchive archive = ZipFile.Open(archivePath, ZipArchiveMode.Create))
        {
            archive.CreateEntry("readme.txt");
        }

        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => store.ImportAsync(archivePath));
    }

    [TestMethod]
    public async Task DeleteAsync_WithAVersion_RemovesOnlyThatVersion()
    {
        BaselinePackageManifest first = await CaptureAsync("Orders upgrade");
        await CaptureAsync("Orders upgrade");

        await store.DeleteAsync(first.Id, first.Version);

        BaselineSummary remaining = (await store.ListAsync()).Single();
        Assert.AreEqual(2, remaining.Version);
    }

    [TestMethod]
    public async Task ListAsync_SkipsAPackageWhoseManifestIsUnreadable()
    {
        BaselinePackageManifest good = await CaptureAsync("Orders upgrade");
        BaselinePackageManifest broken = await CaptureAsync("Payments upgrade");
        await File.WriteAllTextAsync(
            Path.Combine(workspaceRoot, "baselines", broken.Id.Value, $"v{broken.Version}", "baseline.json"),
            "{ not json");

        IReadOnlyList<BaselineSummary> baselines = await store.ListAsync();

        // One hand-edited package must not hide the rest of the library.
        Assert.AreEqual(good.Id, baselines.Single().Id);
    }

    private async Task<BaselinePackageManifest> CaptureAsync(string name)
    {
        BaselinePackageManifest reserved = await store.BeginCaptureAsync(CreateRequest(name));
        await store.AppendScenarioAsync(reserved.Id, reserved.Version, CreateScenario("orders/one.xml"));
        return await store.CompleteCaptureAsync(reserved.Id, reserved.Version);
    }

    private static BaselineCaptureRequest CreateRequest(string name = "Orders upgrade") =>
        new BaselineCaptureRequest(
            name,
            new Uri("https://legacy.example.test/lookup"),
            "client.lookup",
            "client.lookup.customer",
            new DateTimeOffset(2026, 3, 1, 9, 0, 0, TimeSpan.Zero),
            "run-1",
            "2.1.0",
            "staging",
            "Legacy",
            "rules-hash",
            new ComparisonOptions(ignoreCollectionOrder: true));

    private static BaselineScenarioCapture CreateScenario(
        string relativePath,
        string request = "<request />",
        string canonical = "{\"status\":\"OK\"}",
        string raw = "<response />") =>
        new BaselineScenarioCapture(
            relativePath,
            "application/xml",
            new Dictionary<string, string> { ["SOAPAction"] = "urn:lookup" },
            200,
            "text/xml",
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(request))),
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(canonical))),
            _ => Task.FromResult<Stream>(new MemoryStream(Encoding.UTF8.GetBytes(raw))));
}
