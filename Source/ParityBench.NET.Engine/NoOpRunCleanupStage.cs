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

    public Task CleanupAsync(
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

        // PR2 only wires append-before-delete flow. Retention policy actions are implemented in PR3.
        return Task.CompletedTask;
    }
}
