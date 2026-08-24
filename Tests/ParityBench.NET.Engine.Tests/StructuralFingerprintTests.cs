using System.Text;
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Engine;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class StructuralFingerprintTests
{
    [TestMethod]
    public async Task ExportAsync_WritesOnlyAggregateCountsAndSaltedIdentifierHashes()
    {
        const string secretPath = "Customer.SecretToken";
        const string secretValue = "top-secret-response-value";
        string directory = Path.Combine(Path.GetTempPath(), "ParityBenchNET.FingerprintTests", Guid.NewGuid().ToString("N"));
        try
        {
            StructuralFingerprintCollector collector = new();
            collector.RecordNode(secretPath.AsSpan(), typeof(SecretModel), depth: 2);
            collector.RecordCollection(17);
            collector.RecordScalar(Encoding.UTF8.GetByteCount(secretValue));
            collector.RecordSortKey(1234);
            NormalizationWorkMetrics work = new(
                TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                1, 2, 3, 17, 4, 25, 5, 1234, 1234, 6, 7, 8);

            string path = await StructuralFingerprintExporter.ExportAsync(
                directory,
                new RunId("privacy-test"),
                collector.Snapshot(work),
                new RunExecutionMetrics(
                    TimeSpan.FromSeconds(1), TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                    1, 1, 0,
                    detailedCompareMetrics: new DetailedCompareMetrics(
                        TimeSpan.Zero, 0, TimeSpan.Zero, TimeSpan.FromMilliseconds(10), TimeSpan.Zero,
                        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero,
                        TimeSpan.Zero, TimeSpan.Zero, TimeSpan.Zero),
                    processResourceMetrics: new RunProcessResourceMetrics(
                        TimeSpan.Zero, 0, 0, 0, 0, 64, 0, 0, 0, 1)),
                CancellationToken.None);
            string json = await File.ReadAllTextAsync(path);
            StructuralFingerprintExport? export = JsonSerializer.Deserialize<StructuralFingerprintExport>(json);

            Assert.IsFalse(json.Contains(secretPath, StringComparison.Ordinal));
            Assert.IsFalse(json.Contains(nameof(SecretModel), StringComparison.Ordinal));
            Assert.IsFalse(json.Contains(secretValue, StringComparison.Ordinal));
            Assert.IsNotNull(export);
            StructuralFingerprintSnapshot snapshot = export.Structure;
            Assert.AreEqual(1, snapshot.HashedPathFrequencies.Count);
            Assert.IsTrue(snapshot.HashedPathFrequencies.Keys.All(hash => hash.Length == 24));
            Assert.AreEqual(17, snapshot.CollectionItemCount);
            Assert.AreEqual(1234, snapshot.SortKeyBytes);
            Assert.AreEqual(64, export.PerformanceEvidence.ManagedAllocatedBytesPerPair);
            Assert.AreEqual(10, export.PerformanceEvidence.NormalizationMillisecondsPerPair);
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class SecretModel;
}
