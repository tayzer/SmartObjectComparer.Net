using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine.Pipeline;

public sealed record PlannedRequest(
    int ManifestOrdinal,
    RequestItem Request);

public sealed record ComparedExecutionRecord(
    int ManifestOrdinal,
    RequestPairResult Result);

public sealed record MappedExecutionRecord(
    ExecutionRecord Execution,
    RequestPairResult? TerminalResult = null,
    long ExecutedEnqueuedTimestamp = 0);

public sealed record CleanupStageContext(
    RunOptions ComparisonOptions,
    RunDetailReference DetailReference,
    IReadOnlyList<ComparedExecutionRecord> PersistedRecords,
    bool DurableAppendCompleted);

public sealed record CleanupStageResult(
    int RetainedArtifactCount,
    int TrimmedByPolicyArtifactCount,
    int MissingUnexpectedlyArtifactCount)
{
    public static readonly CleanupStageResult Empty = new(0, 0, 0);
}

public interface IRunCleanupStage
{
    Task<CleanupStageResult> CleanupAsync(
        ComparisonRun run,
        CleanupStageContext context,
        CancellationToken cancellationToken = default);
}
