namespace ParityBench.NET.Domain.Runs;

public sealed record RunEvent
{
    public RunEvent(
        RunId runId,
        RunStatus status,
        RunProgress progress,
        DateTimeOffset timestamp,
        string? errorMessage = null)
    {
        ArgumentNullException.ThrowIfNull(progress);

        RunId = runId;
        Status = status;
        Progress = progress;
        Timestamp = timestamp;
        ErrorMessage = string.IsNullOrWhiteSpace(errorMessage) ? null : errorMessage;
    }

    public RunId RunId { get; }

    public RunStatus Status { get; }

    public RunProgress Progress { get; }

    public DateTimeOffset Timestamp { get; }

    public string? ErrorMessage { get; }
}
