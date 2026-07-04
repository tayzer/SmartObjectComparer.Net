namespace ParityBench.NET.Domain.Runs;

public sealed record ComparisonRun
{
    private ComparisonRun(
        RunId id,
        RunOptions options,
        RunStatus status,
        RunProgress progress,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        RunResultSummary? summary,
        string? errorMessage)
    {
        Id = id;
        Options = options;
        Status = status;
        Progress = progress;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        Summary = summary;
        ErrorMessage = errorMessage;
    }

    public RunId Id { get; init; }

    public RunOptions Options { get; init; }

    public RunStatus Status { get; init; }

    public RunProgress Progress { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public RunResultSummary? Summary { get; init; }

    public string? ErrorMessage { get; init; }

    public bool IsTerminal => Status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled;

    public static ComparisonRun Create(RunId id, RunOptions options, DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        DateTimeOffset timestamp = createdAt ?? DateTimeOffset.UtcNow;
        return new ComparisonRun(
            id,
            options,
            RunStatus.Created,
            new RunProgress(0, "Run created."),
            timestamp,
            timestamp,
            null,
            null,
            null,
            null);
    }

    public static ComparisonRun Rehydrate(
        RunId id,
        RunOptions options,
        RunStatus status,
        RunProgress progress,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        DateTimeOffset? startedAt,
        DateTimeOffset? completedAt,
        RunResultSummary? summary,
        string? errorMessage)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);

        return new ComparisonRun(
            id,
            options,
            status,
            progress,
            createdAt,
            updatedAt,
            startedAt,
            completedAt,
            summary,
            string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage);
    }

    public ComparisonRun Start(DateTimeOffset? startedAt = null)
    {
        EnsureNotTerminal();

        DateTimeOffset timestamp = startedAt ?? DateTimeOffset.UtcNow;
        return this with
        {
            Status = RunStatus.Executing,
            Progress = new RunProgress(0, "Run started."),
            UpdatedAt = timestamp,
            StartedAt = StartedAt ?? timestamp,
            ErrorMessage = null,
        };
    }

    public ComparisonRun Advance(
        RunStatus status,
        int percentComplete,
        string message,
        int? completedItems = null,
        int? totalItems = null,
        DateTimeOffset? updatedAt = null) =>
        Advance(status, new RunProgress(percentComplete, message, completedItems, totalItems), updatedAt);

    public ComparisonRun Advance(RunStatus status, RunProgress progress, DateTimeOffset? updatedAt = null)
    {
        ArgumentNullException.ThrowIfNull(progress);
        EnsureNotTerminal();
        EnsureActiveStatus(status);

        DateTimeOffset timestamp = updatedAt ?? DateTimeOffset.UtcNow;
        return this with
        {
            Status = status,
            Progress = progress,
            UpdatedAt = timestamp,
        };
    }

    public ComparisonRun Complete(RunResultSummary summary, DateTimeOffset? completedAt = null)
    {
        ArgumentNullException.ThrowIfNull(summary);
        EnsureNotTerminal();

        DateTimeOffset timestamp = completedAt ?? DateTimeOffset.UtcNow;
        return this with
        {
            Status = RunStatus.Completed,
            Progress = new RunProgress(100, "Run completed."),
            UpdatedAt = timestamp,
            CompletedAt = timestamp,
            Summary = summary,
            ErrorMessage = null,
        };
    }

    public ComparisonRun Fail(string errorMessage, DateTimeOffset? failedAt = null)
    {
        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new ArgumentException("Error message must not be empty.", nameof(errorMessage));
        }

        EnsureNotTerminal();

        DateTimeOffset timestamp = failedAt ?? DateTimeOffset.UtcNow;
        return this with
        {
            Status = RunStatus.Failed,
            Progress = new RunProgress(Progress.PercentComplete, errorMessage, Progress.CompletedItems, Progress.TotalItems),
            UpdatedAt = timestamp,
            CompletedAt = timestamp,
            ErrorMessage = errorMessage,
        };
    }

    public ComparisonRun Cancel(string? message = null, DateTimeOffset? cancelledAt = null)
    {
        EnsureNotTerminal();

        DateTimeOffset timestamp = cancelledAt ?? DateTimeOffset.UtcNow;
        return this with
        {
            Status = RunStatus.Cancelled,
            Progress = new RunProgress(Progress.PercentComplete, message ?? "Run was cancelled.", Progress.CompletedItems, Progress.TotalItems),
            UpdatedAt = timestamp,
            CompletedAt = timestamp,
        };
    }

    private void EnsureNotTerminal()
    {
        if (IsTerminal)
        {
            throw new InvalidRunStateException($"Run '{Id}' is already terminal with status '{Status}'.");
        }
    }

    private static void EnsureActiveStatus(RunStatus status)
    {
        if (status is RunStatus.Completed or RunStatus.Failed or RunStatus.Cancelled)
        {
            throw new InvalidRunStateException($"Use the dedicated transition method for terminal status '{status}'.");
        }
    }
}
