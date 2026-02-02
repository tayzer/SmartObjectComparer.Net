// <copyright file="PerformanceTracker.cs" company="PlaceholderCompany">



using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility for tracking and reporting performance metrics across the entire application.
/// </summary>
public class PerformanceTracker : IDisposable
{
    private readonly ILogger<PerformanceTracker> logger;
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> timings = new ConcurrentDictionary<string, ConcurrentBag<long>>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> counts = new ConcurrentDictionary<string, int>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, long> totalTimes = new ConcurrentDictionary<string, long>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Stopwatch> activeOperations = new ConcurrentDictionary<string, Stopwatch>(StringComparer.Ordinal);

    public PerformanceTracker(ILogger<PerformanceTracker> logger) => this.logger = logger;

    /// <summary>
    /// Starts tracking a new operation.
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <returns>Operation ID for stopping later.</returns>
    public string StartOperation(string operationName)
    {
        var id = $"{operationName}_{Guid.NewGuid():N}";
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        activeOperations[id] = stopwatch;
        return id;
    }

    /// <summary>
    /// Stops tracking an operation and records its timing.
    /// </summary>
    /// <param name="operationId">Operation ID returned from StartOperation.</param>
    public void StopOperation(string operationId)
    {
        if (!activeOperations.TryRemove(operationId, out var stopwatch))
        {
            logger.LogWarning("Attempted to stop unknown operation: {OperationId}", operationId);
            return;
        }

        stopwatch.Stop();
        var operationName = operationId.Substring(0, operationId.LastIndexOf('_'));
        RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Tracks an entire operation from start to finish.
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <param name="action">The action to execute and time.</param>
    public void TrackOperation(string operationName, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an operation that returns a value.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <param name="func">The function to execute and time.</param>
    /// <returns>The value returned by the operation.</returns>
    public T TrackOperation<T>(string operationName, Func<T> func)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return func();
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an async operation.
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <param name="task">The task to execute and time.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task TrackOperationAsync(string operationName, Func<Task> task)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await task().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an async operation that returns a value.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <param name="task">The task to execute and time.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> task)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await task().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Gets all metrics for tracked operations.
    /// </summary>
    /// <returns>A dictionary of operation metrics keyed by operation name.</returns>
    public Dictionary<string, OperationMetrics> GetMetrics()
    {
        var result = new Dictionary<string, OperationMetrics>(StringComparer.Ordinal);

        foreach (var operation in timings.Keys)
        {
            var timings = this.timings[operation].ToArray();
            if (timings.Length == 0)
            {
                continue;
            }

            var metrics = new OperationMetrics
            {
                OperationName = operation,
                CallCount = counts.GetValueOrDefault(operation),
                TotalTimeMs = totalTimes.GetValueOrDefault(operation),
                AverageTimeMs = timings.Length > 0 ? timings.Average() : 0,
                MinTimeMs = timings.Length > 0 ? timings.Min() : 0,
                MaxTimeMs = timings.Length > 0 ? timings.Max() : 0,
                MedianTimeMs = timings.Length > 0 ? CalculateMedian(timings) : 0,
            };

            result[operation] = metrics;
        }

        return result;
    }

    /// <summary>
    /// Gets a performance report for all tracked operations.
    /// </summary>
    /// <returns>A formatted performance report string.</returns>
    public string GetReport()
    {
        var metrics = GetMetrics();
        if (!metrics.Any())
        {
            return "No performance data collected.";
        }

        var report = new System.Text.StringBuilder();
        report.AppendLine("SYSTEM PERFORMANCE REPORT");
        report.AppendLine("=========================");
        report.AppendLine();

        foreach (var metric in metrics.Values.OrderByDescending(m => m.TotalTimeMs))
        {
            report.AppendLine($"Operation: {metric.OperationName}");
            report.AppendLine($"  Calls:       {metric.CallCount}");
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Total Time:  {0}ms", metric.TotalTimeMs));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Average:     {0:F2}ms", metric.AverageTimeMs));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Median:      {0:F2}ms", metric.MedianTimeMs));
            report.AppendLine(string.Format(CultureInfo.InvariantCulture, "  Min/Max:     {0}ms / {1}ms", metric.MinTimeMs, metric.MaxTimeMs));
            report.AppendLine();
        }

        return report.ToString();
    }

    /// <summary>
    /// Logs a performance report to the logger.
    /// </summary>
    public void LogReport()
    {
        var metrics = GetMetrics();
        if (!metrics.Any())
        {
            logger.LogInformation("No performance data collected.");
            return;
        }

        logger.LogInformation("SYSTEM PERFORMANCE REPORT");

        foreach (var metric in metrics.Values.OrderByDescending(m => m.TotalTimeMs))
        {
            logger.LogInformation(
                "{Operation}: Calls={Count}, Total={Total}ms, Avg={Avg:F2}ms, Median={Median:F2}ms, Min/Max={Min}/{Max}ms",
                metric.OperationName,
                metric.CallCount,
                metric.TotalTimeMs,
                metric.AverageTimeMs,
                metric.MedianTimeMs,
                metric.MinTimeMs,
                metric.MaxTimeMs);
        }
    }

    /// <summary>
    /// Saves the performance report to a file.
    /// </summary>
    /// <param name="filePath">Path where the report should be saved. If null, will use a default timestamped path in the current directory.</param>
    /// <returns>The path to the saved file.</returns>
    public string SaveReportToFile(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.txt");
        }

        var report = GetReport();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, report);
        logger.LogInformation("Performance report saved to: {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Saves the performance report as CSV for further analysis.
    /// </summary>
    /// <param name="filePath">Path where the CSV should be saved. If null, will use a default timestamped path.</param>
    /// <returns>The path to the saved CSV file.</returns>
    public string SaveReportToCsv(string? filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.csv");
        }

        var metrics = GetMetrics();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using (var writer = new StreamWriter(filePath))
        {
            // Write header
            writer.WriteLine("Operation,CallCount,TotalTimeMs,AverageTimeMs,MedianTimeMs,MinTimeMs,MaxTimeMs");

            // Write data rows
            foreach (var metric in metrics.Values.OrderByDescending(m => m.TotalTimeMs))
            {
                writer.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "\"{0}\",{1},{2},{3:F2},{4:F2},{5},{6}",
                        metric.OperationName,
                        metric.CallCount,
                        metric.TotalTimeMs,
                        metric.AverageTimeMs,
                        metric.MedianTimeMs,
                        metric.MinTimeMs,
                        metric.MaxTimeMs));
            }
        }

        logger.LogInformation("Performance report CSV saved to: {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Resets all performance data.
    /// </summary>
    public void Reset()
    {
        timings.Clear();
        counts.Clear();
        totalTimes.Clear();
        activeOperations.Clear();
    }

    public void Dispose()
    {
        // Make sure any in-progress operations are cleaned up
        foreach (var operation in activeOperations)
        {
            operation.Value.Stop();

            var operationName = operation.Key.Substring(0, operation.Key.LastIndexOf('_'));
            RecordTiming(operationName, operation.Value.ElapsedMilliseconds);
        }

        activeOperations.Clear();
    }

    /// <summary>
    /// Records timing for an operation.
    /// </summary>
    private void RecordTiming(string operationName, long elapsedMs)
    {
        timings.GetOrAdd(operationName, _ => new ConcurrentBag<long>()).Add(elapsedMs);
        counts.AddOrUpdate(operationName, 1, (_, count) => count + 1);
        totalTimes.AddOrUpdate(operationName, elapsedMs, (_, total) => total + elapsedMs);

        // Log significant operations (took >1000ms)
        if (elapsedMs > 1000)
        {
            logger.LogInformation("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
        else
        {
            logger.LogDebug("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
    }

    /// <summary>
    /// Calculates the median of a set of values.
    /// </summary>
    private double CalculateMedian(long[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        var mid = sorted.Length / 2;

        if (sorted.Length % 2 == 0)
        {
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        }

        return sorted[mid];
    }
}

/// <summary>
/// Performance metrics for an operation.
/// </summary>
public class OperationMetrics
{
    required public string OperationName
    {
        get; set;
    }

    public int CallCount
    {
        get; set;
    }

    public long TotalTimeMs
    {
        get; set;
    }

    public double AverageTimeMs
    {
        get; set;
    }

    public double MedianTimeMs
    {
        get; set;
    }

    public long MinTimeMs
    {
        get; set;
    }

    public long MaxTimeMs
    {
        get; set;
    }
}
