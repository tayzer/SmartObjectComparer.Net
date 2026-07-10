using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Engine.Pipeline;

public sealed record PlannedRequest(
    int ManifestOrdinal,
    RequestItem Request);

public sealed record ComparedExecutionRecord(
    int ManifestOrdinal,
    RequestPairResult Result);

public sealed record CleanupStageContext(
    RunOptions ComparisonOptions,
    RunDetailReference DetailReference,
    IReadOnlyList<ComparedExecutionRecord> PersistedRecords,
    bool DurableAppendCompleted);

public interface IRunCleanupStage
{
    Task CleanupAsync(
        ComparisonRun run,
        CleanupStageContext context,
        CancellationToken cancellationToken = default);
}