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

        return builder.CreateSnapshot(
            Math.Max(0, options.MaxSlowPathEntries),
            Math.Max(0, options.MaxExceptionEntries));
    }

    private RunDiagnosticsBuilder GetDiagnostics(RunId runId) =>
        diagnostics.GetOrAdd(runId, _ => new RunDiagnosticsBuilder());

    private sealed class RunDiagnosticsBuilder
    {
        private readonly object gate = new object();
        private readonly List<SlowRequestPathDiagnostic> slowPaths = new List<SlowRequestPathDiagnostic>();
        private readonly List<ExceptionDiagnostic> exceptions = new List<ExceptionDiagnostic>();

        public void AddSlowPath(SlowRequestPathDiagnostic slowPath)
        {
            lock (gate)
            {
                slowPaths.Add(slowPath);
            }
        }

        public void AddException(ExceptionDiagnostic exception)
        {
            lock (gate)
            {
                exceptions.Add(exception);
            }
        }

        public RunDiagnosticsSnapshot? CreateSnapshot(int maxSlowPathEntries, int maxExceptionEntries)
        {
            lock (gate)
            {
                List<SlowRequestPathDiagnostic> selectedSlowPaths = slowPaths
                    .OrderByDescending(path => path.Duration)
                    .Take(maxSlowPathEntries)
                    .ToList();
                List<ExceptionDiagnostic> selectedExceptions = exceptions
                    .Take(maxExceptionEntries)
                    .ToList();

                if (selectedSlowPaths.Count == 0 && selectedExceptions.Count == 0)
                {
                    return null;
                }

                return new RunDiagnosticsSnapshot(selectedSlowPaths, selectedExceptions);
            }
        }
    }
}
