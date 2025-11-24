// <copyright file="PerformanceTracker.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility for tracking and reporting performance metrics across the entire application.
/// </summary>
public class PerformanceTracker : IDisposable
{
    private readonly ILogger<PerformanceTracker> logger;
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> timings = new ();
    private readonly ConcurrentDictionary<string, int> counts = new ();
    private readonly ConcurrentDictionary<string, long> totalTimes = new ();
    private readonly ConcurrentDictionary<string, Stopwatch> activeOperations = new ();

    public PerformanceTracker(ILogger<PerformanceTracker> logger)
    {
        this.logger = logger;
    }

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
        this.activeOperations[id] = stopwatch;
        return id;
    }

    /// <summary>
    /// Stops tracking an operation and records its timing.
    /// </summary>
    /// <param name="operationId">Operation ID returned from StartOperation.</param>
    public void StopOperation(string operationId)
    {
        if (!this.activeOperations.TryRemove(operationId, out var stopwatch))
        {
            this.logger.LogWarning("Attempted to stop unknown operation: {OperationId}", operationId);
            return;
        }

        stopwatch.Stop();
        var operationName = operationId.Substring(0, operationId.LastIndexOf('_'));
        this.RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Tracks an entire operation from start to finish.
    /// </summary>
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
            this.RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an operation that returns a value.
    /// </summary>
    /// <returns></returns>
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
            this.RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an async operation.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task TrackOperationAsync(string operationName, Func<Task> task)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await task();
        }
        finally
        {
            stopwatch.Stop();
            this.RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Tracks an async operation that returns a value.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<T> TrackOperationAsync<T>(string operationName, Func<Task<T>> task)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            return await task();
        }
        finally
        {
            stopwatch.Stop();
            this.RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Records timing for an operation.
    /// </summary>
    private void RecordTiming(string operationName, long elapsedMs)
    {
        this.timings.GetOrAdd(operationName, _ => new ConcurrentBag<long>()).Add(elapsedMs);
        this.counts.AddOrUpdate(operationName, 1, (_, count) => count + 1);
        this.totalTimes.AddOrUpdate(operationName, elapsedMs, (_, total) => total + elapsedMs);

        // Log significant operations (took >1000ms)
        if (elapsedMs > 1000)
        {
            this.logger.LogInformation("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
        else
        {
            this.logger.LogDebug("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
    }

    /// <summary>
    /// Gets all metrics for tracked operations.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, OperationMetrics> GetMetrics()
    {
        var result = new Dictionary<string, OperationMetrics>();

        foreach (var operation in this.timings.Keys)
        {
            var timings = this.timings[operation].ToArray();
            if (timings.Length == 0)
            {
                continue;
            }

            var metrics = new OperationMetrics
            {
                OperationName = operation,
                CallCount = this.counts.GetValueOrDefault(operation),
                TotalTimeMs = this.totalTimes.GetValueOrDefault(operation),
                AverageTimeMs = timings.Length > 0 ? timings.Average() : 0,
                MinTimeMs = timings.Length > 0 ? timings.Min() : 0,
                MaxTimeMs = timings.Length > 0 ? timings.Max() : 0,
                MedianTimeMs = timings.Length > 0 ? this.CalculateMedian(timings) : 0,
            };

            result[operation] = metrics;
        }

        return result;
    }

    /// <summary>
    /// Gets a performance report for all tracked operations.
    /// </summary>
    /// <returns></returns>
    public string GetReport()
    {
        var metrics = this.GetMetrics();
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
            report.AppendLine($"  Total Time:  {metric.TotalTimeMs}ms");
            report.AppendLine($"  Average:     {metric.AverageTimeMs:F2}ms");
            report.AppendLine($"  Median:      {metric.MedianTimeMs:F2}ms");
            report.AppendLine($"  Min/Max:     {metric.MinTimeMs}ms / {metric.MaxTimeMs}ms");
            report.AppendLine();
        }

        return report.ToString();
    }

    /// <summary>
    /// Logs a performance report to the logger.
    /// </summary>
    public void LogReport()
    {
        var metrics = this.GetMetrics();
        if (!metrics.Any())
        {
            this.logger.LogInformation("No performance data collected.");
            return;
        }

        this.logger.LogInformation("SYSTEM PERFORMANCE REPORT");

        foreach (var metric in metrics.Values.OrderByDescending(m => m.TotalTimeMs))
        {
            this.logger.LogInformation(
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
    public string SaveReportToFile(string filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.txt");
        }

        var report = this.GetReport();

        // Ensure directory exists
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, report);
        this.logger.LogInformation("Performance report saved to: {FilePath}", filePath);

        return filePath;
    }

    /// <summary>
    /// Saves the performance report as CSV for further analysis.
    /// </summary>
    /// <param name="filePath">Path where the CSV should be saved. If null, will use a default timestamped path.</param>
    /// <returns>The path to the saved CSV file.</returns>
    public string SaveReportToCsv(string filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.csv");
        }

        var metrics = this.GetMetrics();

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
                    $"\"{metric.OperationName}\",{metric.CallCount},{metric.TotalTimeMs},{metric.AverageTimeMs:F2},{metric.MedianTimeMs:F2},{metric.MinTimeMs},{metric.MaxTimeMs}");
            }
        }

        this.logger.LogInformation("Performance report CSV saved to: {FilePath}", filePath);

        return filePath;
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

    /// <summary>
    /// Resets all performance data.
    /// </summary>
    public void Reset()
    {
        this.timings.Clear();
        this.counts.Clear();
        this.totalTimes.Clear();
        this.activeOperations.Clear();
    }

    public void Dispose()
    {
        // Make sure any in-progress operations are cleaned up
        foreach (var operation in this.activeOperations)
        {
            operation.Value.Stop();

            var operationName = operation.Key.Substring(0, operation.Key.LastIndexOf('_'));
            this.RecordTiming(operationName, operation.Value.ElapsedMilliseconds);
        }

        this.activeOperations.Clear();
    }
}

/// <summary>
/// Performance metrics for an operation.
/// </summary>
public class OperationMetrics
{
    public string OperationName
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
