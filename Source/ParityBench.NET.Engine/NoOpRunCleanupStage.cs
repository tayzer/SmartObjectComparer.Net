using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using ParityBench.NET.Application.ContractProfiles;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;
using ParityBench.NET.Engine.Comparers;
using ParityBench.NET.Engine.Pipeline;

namespace ParityBench.NET.Engine;

internal sealed class NoOpRunCleanupStage : IRunCleanupStage
{
    public static NoOpRunCleanupStage Instance { get; } = new NoOpRunCleanupStage();

    private NoOpRunCleanupStage()
    {
    }

    public Task<CleanupStageResult> CleanupAsync(
        ComparisonRun run,
        CleanupStageContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(context);
        if (!context.DurableAppendCompleted)
        {
            throw new InvalidOperationException("Cleanup requires durable append completion.");
        }

        int retained = context.PersistedRecords.Sum(record => CountArtifacts(record.Result));
        return Task.FromResult(new CleanupStageResult(retained, 0, 0));
    }

    private static int CountArtifacts(RequestPairResult result) =>
        (result.ResponseA is null ? 0 : 1)
        + (result.ResponseB is null ? 0 : 1)
        + (result.FocusedResponseA is null ? 0 : 1)
        + (result.FocusedResponseB is null ? 0 : 1)
        + (result.ResponseA?.Artifact.ArtifactId.Contains("/canonical/", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0)
        + (result.ResponseB?.Artifact.ArtifactId.Contains("/canonical/", StringComparison.OrdinalIgnoreCase) == true ? 1 : 0);
}
