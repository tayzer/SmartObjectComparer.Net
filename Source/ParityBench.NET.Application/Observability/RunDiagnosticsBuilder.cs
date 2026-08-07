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
    private readonly int maxSlowPathEntries;
    private readonly int maxExceptionEntries;

    public RunDiagnosticsBuilder(int maxSlowPathEntries, int maxExceptionEntries)
    {
        this.maxSlowPathEntries = Math.Max(0, maxSlowPathEntries);
        this.maxExceptionEntries = Math.Max(0, maxExceptionEntries);
    }

    public void AddSlowPath(SlowRequestPathDiagnostic slowPath)
    {
        lock (gate)
        {
            if (maxSlowPathEntries == 0)
            {
                return;
            }

            if (slowPaths.Count < maxSlowPathEntries)
            {
                slowPaths.Add(slowPath);
                return;
            }

            int slowestIndex = 0;
            for (int index = 1; index < slowPaths.Count; index++)
            {
                if (slowPaths[index].Duration < slowPaths[slowestIndex].Duration)
                {
                    slowestIndex = index;
                }
            }

            if (slowPath.Duration > slowPaths[slowestIndex].Duration)
            {
                slowPaths[slowestIndex] = slowPath;
            }
        }
    }

    public void AddException(ExceptionDiagnostic exception)
    {
        lock (gate)
        {
            if (exceptions.Count < maxExceptionEntries)
            {
                exceptions.Add(exception);
            }
        }
    }

    public RunDiagnosticsSnapshot? CreateSnapshot()
    {
        lock (gate)
        {
            List<SlowRequestPathDiagnostic> selectedSlowPaths = slowPaths
                .OrderByDescending(path => path.Duration)
                .ToList();
            List<ExceptionDiagnostic> selectedExceptions = exceptions
                .ToList();

            if (selectedSlowPaths.Count == 0 && selectedExceptions.Count == 0)
            {
                return null;
            }

            return new RunDiagnosticsSnapshot(selectedSlowPaths, selectedExceptions);
        }
    }
}
