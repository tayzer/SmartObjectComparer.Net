using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility for tracking and reporting performance metrics across the entire application
/// </summary>
public class PerformanceTracker : IDisposable
{
    private readonly ILogger<PerformanceTracker> _logger;
    private readonly ConcurrentDictionary<string, ConcurrentBag<long>> _timings = new();
    private readonly ConcurrentDictionary<string, int> _counts = new();
    private readonly ConcurrentDictionary<string, long> _totalTimes = new();
    private readonly ConcurrentDictionary<string, Stopwatch> _activeOperations = new();
    
    public PerformanceTracker(ILogger<PerformanceTracker> logger)
    {
        _logger = logger;
    }
    
    /// <summary>
    /// Starts tracking a new operation
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type</param>
    /// <returns>Operation ID for stopping later</returns>
    public string StartOperation(string operationName)
    {
        string id = $"{operationName}_{Guid.NewGuid():N}";
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        _activeOperations[id] = stopwatch;
        return id;
    }
    
    /// <summary>
    /// Stops tracking an operation and records its timing
    /// </summary>
    /// <param name="operationId">Operation ID returned from StartOperation</param>
    public void StopOperation(string operationId)
    {
        if (!_activeOperations.TryRemove(operationId, out var stopwatch))
        {
            _logger.LogWarning("Attempted to stop unknown operation: {OperationId}", operationId);
            return;
        }
        
        stopwatch.Stop();
        string operationName = operationId.Substring(0, operationId.LastIndexOf('_'));
        RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
    }
    
    /// <summary>
    /// Tracks an entire operation from start to finish
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
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }
    
    /// <summary>
    /// Tracks an operation that returns a value
    /// </summary>
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
    /// Tracks an async operation
    /// </summary>
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
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }
    
    /// <summary>
    /// Tracks an async operation that returns a value
    /// </summary>
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
            RecordTiming(operationName, stopwatch.ElapsedMilliseconds);
        }
    }
    
    /// <summary>
    /// Records timing for an operation
    /// </summary>
    private void RecordTiming(string operationName, long elapsedMs)
    {
        _timings.GetOrAdd(operationName, _ => new ConcurrentBag<long>()).Add(elapsedMs);
        _counts.AddOrUpdate(operationName, 1, (_, count) => count + 1);
        _totalTimes.AddOrUpdate(operationName, elapsedMs, (_, total) => total + elapsedMs);
        
        // Log significant operations (took >1000ms)
        if (elapsedMs > 1000)
        {
            _logger.LogInformation("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
        else
        {
            _logger.LogDebug("{Operation} completed in {ElapsedMs}ms", operationName, elapsedMs);
        }
    }
    
    /// <summary>
    /// Gets all metrics for tracked operations
    /// </summary>
    public Dictionary<string, OperationMetrics> GetMetrics()
    {
        var result = new Dictionary<string, OperationMetrics>();
        
        foreach (var operation in _timings.Keys)
        {
            var timings = _timings[operation].ToArray();
            if (timings.Length == 0) continue;
            
            var metrics = new OperationMetrics
            {
                OperationName = operation,
                CallCount = _counts.GetValueOrDefault(operation),
                TotalTimeMs = _totalTimes.GetValueOrDefault(operation),
                AverageTimeMs = timings.Length > 0 ? timings.Average() : 0,
                MinTimeMs = timings.Length > 0 ? timings.Min() : 0,
                MaxTimeMs = timings.Length > 0 ? timings.Max() : 0,
                MedianTimeMs = timings.Length > 0 ? CalculateMedian(timings) : 0
            };
            
            result[operation] = metrics;
        }
        
        return result;
    }
    
    /// <summary>
    /// Gets a performance report for all tracked operations
    /// </summary>
    public string GetReport()
    {
        var metrics = GetMetrics();
        if (!metrics.Any())
            return "No performance data collected.";
            
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
    /// Logs a performance report to the logger
    /// </summary>
    public void LogReport()
    {
        var metrics = GetMetrics();
        if (!metrics.Any())
        {
            _logger.LogInformation("No performance data collected.");
            return;
        }
            
        _logger.LogInformation("SYSTEM PERFORMANCE REPORT");
        
        foreach (var metric in metrics.Values.OrderByDescending(m => m.TotalTimeMs))
        {
            _logger.LogInformation(
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
    /// Saves the performance report to a file
    /// </summary>
    /// <param name="filePath">Path where the report should be saved. If null, will use a default timestamped path in the current directory.</param>
    /// <returns>The path to the saved file</returns>
    public string SaveReportToFile(string filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
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
        _logger.LogInformation("Performance report saved to: {FilePath}", filePath);
        
        return filePath;
    }
    
    /// <summary>
    /// Saves the performance report as CSV for further analysis
    /// </summary>
    /// <param name="filePath">Path where the CSV should be saved. If null, will use a default timestamped path.</param>
    /// <returns>The path to the saved CSV file</returns>
    public string SaveReportToCsv(string filePath = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
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
                    $"\"{metric.OperationName}\",{metric.CallCount},{metric.TotalTimeMs},{metric.AverageTimeMs:F2},{metric.MedianTimeMs:F2},{metric.MinTimeMs},{metric.MaxTimeMs}");
            }
        }
        
        _logger.LogInformation("Performance report CSV saved to: {FilePath}", filePath);
        
        return filePath;
    }
    
    /// <summary>
    /// Calculates the median of a set of values
    /// </summary>
    private double CalculateMedian(long[] values)
    {
        var sorted = values.OrderBy(v => v).ToArray();
        int mid = sorted.Length / 2;
        
        if (sorted.Length % 2 == 0)
            return (sorted[mid - 1] + sorted[mid]) / 2.0;
        
        return sorted[mid];
    }
    
    /// <summary>
    /// Resets all performance data
    /// </summary>
    public void Reset()
    {
        _timings.Clear();
        _counts.Clear();
        _totalTimes.Clear();
        _activeOperations.Clear();
    }
    
    public void Dispose()
    {
        // Make sure any in-progress operations are cleaned up
        foreach (var operation in _activeOperations)
        {
            operation.Value.Stop();
            
            string operationName = operation.Key.Substring(0, operation.Key.LastIndexOf('_'));
            RecordTiming(operationName, operation.Value.ElapsedMilliseconds);
        }
        
        _activeOperations.Clear();
    }
}

/// <summary>
/// Performance metrics for an operation
/// </summary>
public class OperationMetrics
{
    public string OperationName { get; set; }
    public int CallCount { get; set; }
    public long TotalTimeMs { get; set; }
    public double AverageTimeMs { get; set; }
    public double MedianTimeMs { get; set; }
    public long MinTimeMs { get; set; }
    public long MaxTimeMs { get; set; }
}
