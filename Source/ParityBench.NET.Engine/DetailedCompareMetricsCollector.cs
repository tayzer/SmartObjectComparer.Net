using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine;

/// <summary>Thread-safe per-run counter. Created only when detailed timing is enabled.</summary>
public sealed class DetailedCompareMetricsCollector
{
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

    private static void Add(ref long target, TimeSpan elapsed) => Interlocked.Add(ref target, elapsed.Ticks);
    private static TimeSpan Read(long ticks) => TimeSpan.FromTicks(Interlocked.Read(ref ticks));
}
