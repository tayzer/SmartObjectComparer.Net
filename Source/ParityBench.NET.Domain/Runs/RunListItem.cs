namespace ParityBench.NET.Domain.Runs;

public sealed record RunListItem
{
    public RunListItem(
        RunId id,
        RunStatus status,
        DateTimeOffset createdAt,
        DateTimeOffset updatedAt,
        RunProgress progress,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(progress);

        Id = id;
        Status = status;
        CreatedAt = createdAt;
        UpdatedAt = updatedAt;
        Progress = progress;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
    }

    public RunId Id { get; }

    public RunStatus Status { get; }

    public DateTimeOffset CreatedAt { get; }

    public DateTimeOffset UpdatedAt { get; }

    public RunProgress Progress { get; }

    public string? ErrorMessage { get; }

    public static RunListItem FromRun(ComparisonRun run)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new RunListItem(run.Id, run.Status, run.CreatedAt, run.UpdatedAt, run.Progress, run.ErrorMessage);
    }
}
