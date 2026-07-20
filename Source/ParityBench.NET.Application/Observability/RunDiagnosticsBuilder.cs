using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Observability;

internal sealed class RunDiagnosticsBuilder
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
