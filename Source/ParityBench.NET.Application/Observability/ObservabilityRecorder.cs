using System.Collections.Concurrent;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Observability;

public sealed class ObservabilityRecorder : IObservabilityRecorder
{
    private readonly ILogger<ObservabilityRecorder> logger;
    private readonly ObservabilityOptions options;
    private readonly ConcurrentDictionary<RunId, RunDiagnosticsBuilder> diagnostics = new ConcurrentDictionary<RunId, RunDiagnosticsBuilder>();

    public ObservabilityRecorder(
        ILogger<ObservabilityRecorder> logger,
        IOptions<ObservabilityOptions> options)
    {
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    }

    public bool IsDurationLoggingEnabled => options.LogDurations;

    public bool IsExceptionLoggingEnabled => options.LogExceptions;

    public bool IsDiagnosticsPersistenceEnabled => options.PersistDiagnostics;

    public bool IsDetailedCompareTimingEnabled => options.EnableDetailedCompareTiming;

    public bool IsStructuralFingerprintExportEnabled => options.EnableStructuralFingerprintExport;

    public string? StructuralFingerprintOutputDirectory => options.StructuralFingerprintOutputDirectory;

    public bool IsCalibrationCaptureEnabled => options.CaptureNextRunForCalibration;

    public string? CalibrationCaptureOutputDirectory => options.CalibrationCaptureOutputDirectory;

    public TimeSpan SlowPathThreshold => TimeSpan.FromMilliseconds(Math.Max(0, options.SlowPathThresholdMs));

    public void RecordRunPhase(RunId runId, string phaseName, TimeSpan duration)
    {
        if (IsDurationLoggingEnabled)
        {
            logger.LogInformation(
                "Run {RunId} phase {PhaseName} completed in {DurationMs}ms",
                runId.Value,
                phaseName,
                duration.TotalMilliseconds);
        }
    }

    public void RecordRequestPath(RunId runId, string relativePath, TimeSpan duration)
    {
        if (duration < SlowPathThreshold)
        {
            return;
        }

        if (IsDurationLoggingEnabled)
        {
            logger.LogInformation(
                "Run {RunId} slow request path {RelativePath} completed in {DurationMs}ms",
                runId.Value,
                relativePath,
                duration.TotalMilliseconds);
        }

        if (!IsDiagnosticsPersistenceEnabled)
        {
            return;
        }

        GetDiagnostics(runId).AddSlowPath(new SlowRequestPathDiagnostic(relativePath, duration));
    }

    public void RecordException(
        RunId runId,
        string stage,
        Exception exception,
        string? relativePath = null,
        EndpointSlot? endpoint = null)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (IsExceptionLoggingEnabled)
        {
            logger.LogError(
                exception,
                "Run {RunId} failed during {Stage}. RequestPath={RelativePath}; Endpoint={Endpoint}",
                runId.Value,
                stage,
                relativePath,
                endpoint);
        }

        if (!IsDiagnosticsPersistenceEnabled)
        {
            return;
        }

        GetDiagnostics(runId).AddException(
            new ExceptionDiagnostic(
                stage,
                exception.GetType().FullName ?? exception.GetType().Name,
                exception.Message,
                exception.StackTrace,
                relativePath,
                endpoint));
    }

    public RunDiagnosticsSnapshot? CreateSnapshot(RunId runId)
    {
        if (!IsDiagnosticsPersistenceEnabled || !diagnostics.TryGetValue(runId, out RunDiagnosticsBuilder? builder))
        {
            return null;
        }

        return builder.CreateSnapshot();
    }

    private RunDiagnosticsBuilder GetDiagnostics(RunId runId) =>
        diagnostics.GetOrAdd(runId, _ => new RunDiagnosticsBuilder(
            options.MaxSlowPathEntries,
            options.MaxExceptionEntries));
}
