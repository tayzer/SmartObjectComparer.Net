using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Observability;

public interface IObservabilityRecorder
{
    bool IsDurationLoggingEnabled { get; }

    bool IsExceptionLoggingEnabled { get; }

    bool IsDiagnosticsPersistenceEnabled { get; }

    bool IsDetailedCompareTimingEnabled { get; }

    TimeSpan SlowPathThreshold { get; }

    void RecordRunPhase(RunId runId, string phaseName, TimeSpan duration);

    void RecordRequestPath(RunId runId, string relativePath, TimeSpan duration);

    void RecordException(
        RunId runId,
        string stage,
        Exception exception,
        string? relativePath = null,
        EndpointSlot? endpoint = null);

    RunDiagnosticsSnapshot? CreateSnapshot(RunId runId);
}
