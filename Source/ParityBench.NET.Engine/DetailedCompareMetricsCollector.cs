using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

/// <summary>Thread-safe per-run counter. Created only when detailed timing is enabled.</summary>
public sealed class DetailedCompareMetricsCollector
{
    private readonly StructuralFingerprintCollector? structuralFingerprint;
    private long artifactOpenTicks;
    private long artifactBytesRead;
    private long deserializationTicks;
    private long normalizationTicks;
    private long compareNetObjectsTicks;
    private long materializationTicks;
    private long canonicalMappingTicks;
    private long pluginMappingTicks;
    private long pluginPairProcessingTicks;
    private long focusedContentTicks;
    private long compareQueueWaitTicks;
    private long executionBackpressureTicks;
    private long normalizationTraversalTicks;
    private long normalizationSortKeyTicks;
    private long normalizationSortTicks;
    private long normalizationFallbackTicks;
    private long normalizationRestorationTicks;
    private long objectNodes;
    private long propertyNodes;
    private long collectionNodes;
    private long collectionItems;
    private long scalarNodes;
    private long scalarUtf8Bytes;
    private long ignoredNodes;
    private long sortKeyBytes;
    private long maximumSortKeyBytes;
    private long sortCollisionGroups;
    private long mutableBranches;
    private long legacyFallbackBranches;

    public DetailedCompareMetricsCollector(bool collectStructuralFingerprint = false)
    {
        structuralFingerprint = collectStructuralFingerprint ? new StructuralFingerprintCollector() : null;
    }

    internal StructuralFingerprintCollector? StructuralFingerprint => structuralFingerprint;

    public void AddArtifactOpen(TimeSpan elapsed) => Add(ref artifactOpenTicks, elapsed);
    public void AddArtifactBytesRead(long bytes) => Interlocked.Add(ref artifactBytesRead, bytes);
    public void AddDeserialization(TimeSpan elapsed) => Add(ref deserializationTicks, elapsed);
    public void AddNormalization(TimeSpan elapsed) => Add(ref normalizationTicks, elapsed);
    public void AddCompareNetObjects(TimeSpan elapsed) => Add(ref compareNetObjectsTicks, elapsed);
    public void AddMaterialization(TimeSpan elapsed) => Add(ref materializationTicks, elapsed);
    public void AddCanonicalMapping(TimeSpan elapsed) => Add(ref canonicalMappingTicks, elapsed);
    public void AddPluginMapping(TimeSpan elapsed) => Add(ref pluginMappingTicks, elapsed);
    public void AddPluginPairProcessing(TimeSpan elapsed) => Add(ref pluginPairProcessingTicks, elapsed);
    public void AddFocusedContent(TimeSpan elapsed) => Add(ref focusedContentTicks, elapsed);
    public void AddCompareQueueWait(TimeSpan elapsed) => Add(ref compareQueueWaitTicks, elapsed);
    public void AddExecutionBackpressure(TimeSpan elapsed) => Add(ref executionBackpressureTicks, elapsed);
    public void AddNormalizationTraversal(TimeSpan elapsed) => Add(ref normalizationTraversalTicks, elapsed);
    public void AddNormalizationSortKey(TimeSpan elapsed) => Add(ref normalizationSortKeyTicks, elapsed);
    public void AddNormalizationSort(TimeSpan elapsed) => Add(ref normalizationSortTicks, elapsed);
    public void AddNormalizationFallback(TimeSpan elapsed) => Add(ref normalizationFallbackTicks, elapsed);
    public void AddNormalizationRestoration(TimeSpan elapsed) => Add(ref normalizationRestorationTicks, elapsed);
    public void AddObjectNode(long count = 1) => Interlocked.Add(ref objectNodes, count);
    public void AddPropertyNode(long count = 1) => Interlocked.Add(ref propertyNodes, count);
    public void AddCollectionNode(long itemCount)
    {
        Interlocked.Increment(ref collectionNodes);
        Interlocked.Add(ref collectionItems, itemCount);
    }
    public void AddScalarNode(long utf8Bytes)
    {
        Interlocked.Increment(ref scalarNodes);
        Interlocked.Add(ref scalarUtf8Bytes, utf8Bytes);
    }
    public void AddIgnoredNode(long count = 1) => Interlocked.Add(ref ignoredNodes, count);
    public void AddSortKeyBytes(long bytes)
    {
        Interlocked.Add(ref sortKeyBytes, bytes);
        InterlockedExtensions.Max(ref maximumSortKeyBytes, bytes);
    }
    public void AddSortCollisionGroup(long count = 1) => Interlocked.Add(ref sortCollisionGroups, count);
    public void AddMutableBranch(long count = 1) => Interlocked.Add(ref mutableBranches, count);
    public void AddLegacyFallbackBranch(long count = 1) => Interlocked.Add(ref legacyFallbackBranches, count);

    public void RecordStructureNode(ReadOnlySpan<char> path, Type type, int depth) =>
        structuralFingerprint?.RecordNode(path, type, depth);

    public void RecordCollectionLength(int count) => structuralFingerprint?.RecordCollection(count);
    public void RecordScalarByteLength(int bytes) => structuralFingerprint?.RecordScalar(bytes);
    public void RecordSortKeyByteLength(int bytes) => structuralFingerprint?.RecordSortKey(bytes);

    public DetailedCompareMetrics ToMetrics(TimeSpan comparisonDuration) 
    {
        TimeSpan artifactOpen = Read(artifactOpenTicks);
        TimeSpan deserialization = Read(deserializationTicks);
        TimeSpan normalization = Read(normalizationTicks);
        TimeSpan traversal = Read(compareNetObjectsTicks);
        TimeSpan materialization = Read(materializationTicks);
        TimeSpan canonicalMapping = Read(canonicalMappingTicks);
        TimeSpan pluginMapping = Read(pluginMappingTicks);
        TimeSpan pluginPair = Read(pluginPairProcessingTicks);
        TimeSpan focused = Read(focusedContentTicks);
        TimeSpan classified = artifactOpen + deserialization + normalization + traversal
            + materialization + canonicalMapping + pluginMapping + pluginPair + focused;

        return new DetailedCompareMetrics(
            artifactOpen,
            Interlocked.Read(ref artifactBytesRead),
            deserialization,
            normalization,
            traversal,
            materialization,
            canonicalMapping,
            pluginMapping,
            pluginPair,
            focused,
            comparisonDuration > classified ? comparisonDuration - classified : TimeSpan.Zero,
            Read(compareQueueWaitTicks),
            Read(executionBackpressureTicks));
    }

    public NormalizationWorkMetrics ToNormalizationMetrics() => new(
        Read(normalizationTraversalTicks),
        Read(normalizationSortKeyTicks),
        Read(normalizationSortTicks),
        Read(normalizationFallbackTicks),
        Read(normalizationRestorationTicks),
        Interlocked.Read(ref objectNodes),
        Interlocked.Read(ref propertyNodes),
        Interlocked.Read(ref collectionNodes),
        Interlocked.Read(ref collectionItems),
        Interlocked.Read(ref scalarNodes),
        Interlocked.Read(ref scalarUtf8Bytes),
        Interlocked.Read(ref ignoredNodes),
        Interlocked.Read(ref sortKeyBytes),
        Interlocked.Read(ref maximumSortKeyBytes),
        Interlocked.Read(ref sortCollisionGroups),
        Interlocked.Read(ref mutableBranches),
        Interlocked.Read(ref legacyFallbackBranches));

    private static void Add(ref long target, TimeSpan elapsed) => Interlocked.Add(ref target, elapsed.Ticks);
    private static TimeSpan Read(long ticks) => TimeSpan.FromTicks(Interlocked.Read(ref ticks));
}
