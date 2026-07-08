using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Observability;

public sealed class NoOpObservabilityRecorder : IObservabilityRecorder
{
    public static NoOpObservabilityRecorder Instance { get; } = new NoOpObservabilityRecorder();

    public bool IsDurationLoggingEnabled => false;

    public bool IsExceptionLoggingEnabled => false;

    public bool IsDiagnosticsPersistenceEnabled => false;

    public TimeSpan SlowPathThreshold => TimeSpan.MaxValue;

    public void RecordRunPhase(RunId runId, string phaseName, TimeSpan duration)
    {
    }

    public void RecordRequestPath(RunId runId, string relativePath, TimeSpan duration)
    {
    }

    public void RecordException(
        RunId runId,
        string stage,
        Exception exception,
        string? relativePath = null,
        EndpointSlot? endpoint = null)
    {
    }

    public RunDiagnosticsSnapshot? CreateSnapshot(RunId runId) => null;
}
