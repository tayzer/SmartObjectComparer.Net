using Microsoft.VisualStudio.TestTools.UnitTesting;

using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Workspaces;

namespace ParityBench.NET.Workspaces.Tests;

[TestClass]
public sealed class FileSystemAcceptedDifferenceStoreTests
{
    [TestMethod]
    public async Task SaveAsync_WhenProfileIsSaved_MatchesEquivalentDynamicDifference()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemAcceptedDifferenceStore store = new FileSystemAcceptedDifferenceStore(workspaceRoot);
        ComparisonDifference original = new ComparisonDifference("Orders[0].CustomerId", "123456", "789012", "Changed.");
        ComparisonDifference equivalent = new ComparisonDifference("Orders[3].CustomerId", "111111", "222222", "Changed.");

        AcceptedDifferenceProfile saved = await store.SaveAsync(original, AcceptedDifferenceStatus.AcceptedDifference, "Known variance.");
        IReadOnlyDictionary<string, AcceptedDifferenceProfile> matches = await store.MatchAsync(new[] { equivalent });

        Assert.AreEqual(1, matches.Count);
        Assert.AreEqual(saved.Fingerprint, matches.Values.Single().Fingerprint);
    }

    [TestMethod]
    public async Task ImportAsync_WhenReplaceExisting_ReplacesStoredProfiles()
    {
        string workspaceRoot = CreateTempDirectory();
        FileSystemAcceptedDifferenceStore store = new FileSystemAcceptedDifferenceStore(workspaceRoot);
        await store.SaveAsync(new ComparisonDifference("Customer.Name", "Alice", "Alicia"), AcceptedDifferenceStatus.AcceptedDifference);
        AcceptedDifferenceProfile imported = await store.SaveAsync(new ComparisonDifference("Customer.Status", "Open", "Closed"), AcceptedDifferenceStatus.FixedVerified);
        await store.ClearAsync();

        int importedCount = await store.ImportAsync(new[] { imported }, replaceExisting: true);
        IReadOnlyList<AcceptedDifferenceProfile> profiles = await store.ListAsync();

        Assert.AreEqual(1, importedCount);
        Assert.AreEqual(1, profiles.Count);
        Assert.AreEqual("Customer.Status", profiles[0].NormalizedPropertyPath);
    }

    private static string CreateTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "ParityBenchNET.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}