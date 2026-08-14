using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

internal sealed class StructuralFingerprintCollector
{
    private readonly byte[] salt = RandomNumberGenerator.GetBytes(32);
    private readonly ConcurrentDictionary<string, long> pathHashes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> typeHashes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<int, long> depthDistribution = new();
    private readonly ConcurrentDictionary<int, long> collectionLengthHistogram = new();
    private readonly ConcurrentDictionary<int, long> scalarByteHistogram = new();
    private readonly ConcurrentDictionary<int, long> sortKeyByteHistogram = new();

    public void RecordNode(ReadOnlySpan<char> path, Type type, int depth)
    {
        pathHashes.AddOrUpdate(Hash(path), 1, static (_, count) => count + 1);
        typeHashes.AddOrUpdate(Hash(type.FullName ?? type.Name), 1, static (_, count) => count + 1);
        depthDistribution.AddOrUpdate(Math.Min(depth, 64), 1, static (_, count) => count + 1);
    }

    public void RecordCollection(int count) => RecordHistogram(collectionLengthHistogram, count);
    public void RecordScalar(int bytes) => RecordHistogram(scalarByteHistogram, bytes);
    public void RecordSortKey(int bytes) => RecordHistogram(sortKeyByteHistogram, bytes);

    public StructuralFingerprintSnapshot Snapshot(NormalizationWorkMetrics work) => new(
        SchemaVersion: 1,
        work.ObjectNodeCount,
        work.PropertyNodeCount,
        work.CollectionNodeCount,
        work.CollectionItemCount,
        work.ScalarNodeCount,
        work.ScalarUtf8Bytes,
        work.IgnoredNodeCount,
        work.SortKeyBytes,
        work.MaximumSortKeyBytes,
        work.SortCollisionGroupCount,
        work.MutableBranchCount,
        work.LegacyFallbackBranchCount,
        Snapshot(pathHashes),
        Snapshot(typeHashes),
        Snapshot(depthDistribution),
        Snapshot(collectionLengthHistogram),
        Snapshot(scalarByteHistogram),
        Snapshot(sortKeyByteHistogram));

    private string Hash(ReadOnlySpan<char> value)
    {
        int byteCount = Encoding.UTF8.GetByteCount(value);
        byte[] rented = System.Buffers.ArrayPool<byte>.Shared.Rent(byteCount + salt.Length);
        try
        {
            salt.CopyTo(rented, 0);
            Encoding.UTF8.GetBytes(value, rented.AsSpan(salt.Length, byteCount));
            Span<byte> digest = stackalloc byte[32];
            SHA256.HashData(rented.AsSpan(0, salt.Length + byteCount), digest);
            return Convert.ToHexString(digest[..12]).ToLowerInvariant();
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(rented, clearArray: true);
        }
    }

    private string Hash(string value) => Hash(value.AsSpan());

    private static void RecordHistogram(ConcurrentDictionary<int, long> histogram, int value) =>
        histogram.AddOrUpdate(GetUpperBound(value), 1, static (_, count) => count + 1);

    private static int GetUpperBound(int value)
    {
        if (value <= 1) return Math.Max(0, value);
        int upper = 2;
        while (upper < value && upper < 1 << 30) upper <<= 1;
        return upper;
    }

    private static IReadOnlyDictionary<string, long> Snapshot(ConcurrentDictionary<string, long> source) =>
        source.OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);

    private static IReadOnlyDictionary<int, long> Snapshot(ConcurrentDictionary<int, long> source) =>
        source.OrderBy(item => item.Key).ToDictionary(item => item.Key, item => item.Value);
}

internal sealed record StructuralFingerprintSnapshot(
    int SchemaVersion,
    long ObjectNodeCount,
    long PropertyNodeCount,
    long CollectionNodeCount,
    long CollectionItemCount,
    long ScalarNodeCount,
    long ScalarUtf8Bytes,
    long IgnoredNodeCount,
    long SortKeyBytes,
    long MaximumSortKeyBytes,
    long SortCollisionGroupCount,
    long MutableBranchCount,
    long LegacyFallbackBranchCount,
    IReadOnlyDictionary<string, long> HashedPathFrequencies,
    IReadOnlyDictionary<string, long> HashedTypeFrequencies,
    IReadOnlyDictionary<int, long> DepthDistribution,
    IReadOnlyDictionary<int, long> CollectionLengthHistogram,
    IReadOnlyDictionary<int, long> ScalarByteHistogram,
    IReadOnlyDictionary<int, long> SortKeyByteHistogram);

internal static class StructuralFingerprintExporter
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    public static async Task<string> ExportAsync(
        string? configuredDirectory,
        RunId runId,
        StructuralFingerprintSnapshot fingerprint,
        RunExecutionMetrics executionMetrics,
        CancellationToken cancellationToken)
    {
        string directory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.Combine(Path.GetTempPath(), "ParityBench", "fingerprints")
            : Path.GetFullPath(configuredDirectory);
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, $"structural-fingerprint-{runId.Value}.json");
        await using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.Read,
            16 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        StructuralFingerprintExport export = new(
            DateTimeOffset.UtcNow,
            fingerprint,
            new StructuralFingerprintPerformanceEvidence(
                executionMetrics.RequestCount,
                executionMetrics.RequestCount == 0
                    ? 0
                    : (executionMetrics.ProcessResourceMetrics?.ManagedAllocatedBytes ?? 0) / (double)executionMetrics.RequestCount,
                executionMetrics.RequestCount == 0
                    ? 0
                    : (executionMetrics.DetailedCompareMetrics?.ComparisonModelNormalizationDuration.TotalMilliseconds ?? 0) / executionMetrics.RequestCount));
        await JsonSerializer.SerializeAsync(stream, export, SerializerOptions, cancellationToken).ConfigureAwait(false);
        return path;
    }
}

internal sealed record StructuralFingerprintExport(
    DateTimeOffset CreatedAt,
    StructuralFingerprintSnapshot Structure,
    StructuralFingerprintPerformanceEvidence PerformanceEvidence);

internal sealed record StructuralFingerprintPerformanceEvidence(
    int RequestCount,
    double ManagedAllocatedBytesPerPair,
    double NormalizationMillisecondsPerPair);
