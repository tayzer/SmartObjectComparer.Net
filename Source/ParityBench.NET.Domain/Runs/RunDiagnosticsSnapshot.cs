using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Runs;

public sealed record RunDiagnosticsSnapshot
{
    public RunDiagnosticsSnapshot(
        IReadOnlyList<SlowRequestPathDiagnostic>? slowRequestPaths = null,
        IReadOnlyList<ExceptionDiagnostic>? exceptions = null)
    {
        SlowRequestPaths = (slowRequestPaths ?? Array.Empty<SlowRequestPathDiagnostic>()).ToList();
        Exceptions = (exceptions ?? Array.Empty<ExceptionDiagnostic>()).ToList();
    }

    public IReadOnlyList<SlowRequestPathDiagnostic> SlowRequestPaths { get; }

    public IReadOnlyList<ExceptionDiagnostic> Exceptions { get; }
}

public sealed record SlowRequestPathDiagnostic
{
    public SlowRequestPathDiagnostic(string relativePath, TimeSpan duration)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Relative path must not be empty.", nameof(relativePath));
        }

        if (duration < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(duration), "Duration values must not be negative.");
        }

        RelativePath = relativePath;
        Duration = duration;
    }

    public string RelativePath { get; }

    public TimeSpan Duration { get; }
}

public sealed record ExceptionDiagnostic
{
    public ExceptionDiagnostic(
        string stage,
        string exceptionType,
        string message,
        string? stackTrace = null,
        string? relativePath = null,
        EndpointSlot? endpoint = null)
    {
        if (string.IsNullOrWhiteSpace(stage))
        {
            throw new ArgumentException("Stage must not be empty.", nameof(stage));
        }

        if (string.IsNullOrWhiteSpace(exceptionType))
        {
            throw new ArgumentException("Exception type must not be empty.", nameof(exceptionType));
        }

        Stage = stage;
        ExceptionType = exceptionType;
        Message = message;
        StackTrace = string.IsNullOrWhiteSpace(stackTrace) ? null : stackTrace;
        RelativePath = string.IsNullOrWhiteSpace(relativePath) ? null : relativePath;
        Endpoint = endpoint;
    }

    public string Stage { get; }

    public string ExceptionType { get; }

    public string Message { get; }

    public string? StackTrace { get; }

    public string? RelativePath { get; }

    public EndpointSlot? Endpoint { get; }
}
