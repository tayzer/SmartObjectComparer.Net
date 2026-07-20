using Microsoft.Extensions.Options;
using ParityBench.NET.Application.Observability;
using ParityBench.NET.Application.Runs.Retention;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs;

internal sealed class RunProgressReporter : IRunProgressReporter
{
    private readonly LargeRunOptions options;
    private readonly Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync;
    private readonly object syncRoot = new object();
    private DateTimeOffset lastPublishedAt = DateTimeOffset.MinValue;
    private int lastPublishedCompletedItems;
    private RunStatus? lastPublishedStatus;

    public RunProgressReporter(
        LargeRunOptions options,
        Func<RunStatus, RunProgress, CancellationToken, Task> reportAsync)
    {
        this.options = options;
        this.reportAsync = reportAsync;
    }

    public Task ReportAsync(
        RunStatus status,
        RunProgress progress,
        CancellationToken cancellationToken = default) =>
        ReportAsync(status, progress, cancellationToken, false);

    public Task ReportAsync(
        RunStatus status,
        RunProgress progress,
        CancellationToken cancellationToken = default,
        bool force = false)
    {
        if (!force && !ShouldPublish(status, progress))
        {
            return Task.CompletedTask;
        }

        return reportAsync(status, progress, cancellationToken);
    }

    private bool ShouldPublish(RunStatus status, RunProgress progress)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        int completed = progress.CompletedItems ?? 0;
        lock (syncRoot)
        {
            bool statusChanged = lastPublishedStatus != status;
            bool itemIntervalReached = completed - lastPublishedCompletedItems >= options.ProgressUpdateItemInterval;
            bool timeIntervalReached = now - lastPublishedAt >= TimeSpan.FromMilliseconds(options.ProgressUpdateMillisecondsInterval);
            if (!statusChanged && !itemIntervalReached && !timeIntervalReached)
            {
                return false;
            }

            lastPublishedAt = now;
            lastPublishedCompletedItems = completed;
            lastPublishedStatus = status;
            return true;
        }
    }
}
