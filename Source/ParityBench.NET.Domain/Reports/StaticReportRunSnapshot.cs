using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportRunSnapshot
{
    public StaticReportRunSnapshot(
        string runId,
        RunOptions options,
        RunStatus status,
        RunProgress progress,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        RunResultSummary? summary = null,
        string? errorMessage = null)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id must not be empty.", nameof(runId));
        }

        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        RunId = runId;
        Options = options;
        Status = status;
        Progress = progress;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Summary = summary;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
    }

    public string RunId { get; }

    public RunOptions Options { get; }

    public RunStatus Status { get; }

    public RunProgress Progress { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public RunResultSummary? Summary { get; }

    public string? ErrorMessage { get; }

    public static StaticReportRunSnapshot FromRun(ComparisonRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new StaticReportRunSnapshot(
            run.Id.Value,
            run.Options,
            run.Status,
            run.Progress,
            run.CreatedAt,
            run.UpdatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.Summary,
            run.ErrorMessage);
    }

    public ComparisonRun ToRun() =>
        ComparisonRun.Rehydrate(
            new RunId(RunId),
            Options,
            Status,
            Progress,
            CreatedAt,
            UpdatedAt,
            StartedAt,
            CompletedAt,
            Summary,
            ErrorMessage);
}
