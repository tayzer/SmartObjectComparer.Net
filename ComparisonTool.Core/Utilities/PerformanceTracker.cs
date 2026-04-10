using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility for tracking and reporting performance metrics across the entire application.
/// </summary>
public class PerformanceTracker : IDisposable
{
    private const long InformationLogThresholdMs = 10000;

    private readonly ILogger<PerformanceTracker> logger;
    private readonly ConcurrentDictionary<OperationKey, ConcurrentBag<long>> timings = new();
    private readonly ConcurrentDictionary<OperationKey, int> counts = new();
    private readonly ConcurrentDictionary<OperationKey, long> totalTimes = new();
    private readonly ConcurrentDictionary<string, ActiveOperation> activeOperations = new(StringComparer.Ordinal);
    private readonly AsyncLocal<ScopeFrame?> currentScope = new();

    public PerformanceTracker(ILogger<PerformanceTracker> logger) => this.logger = logger;

    /// <summary>
    /// Begins a scoped collection session so operations can be reported per top-level run.
    /// </summary>
    /// <param name="scopeId">Unique identifier for the scope.</param>
    /// <returns>A disposable scope handle.</returns>
    public PerformanceTrackingScope BeginScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);

        var previousScope = currentScope.Value;
        currentScope.Value = new ScopeFrame(scopeId);
        return new PerformanceTrackingScope(this, scopeId, previousScope);
    }

    /// <summary>
    /// Starts tracking a new operation.
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <returns>Operation ID for stopping later.</returns>
    public string StartOperation(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);

        var id = $"{operationName}_{Guid.NewGuid():N}";
        activeOperations[id] = new ActiveOperation(ResolveOperationKey(operationName), Stopwatch.StartNew());
        return id;
    }

    /// <summary>
    /// Stops tracking an operation and records its timing.
    /// </summary>
    /// <param name="operationId">Operation ID returned from StartOperation.</param>
    public void StopOperation(string operationId)
    {
        if (!activeOperations.TryRemove(operationId, out var activeOperation))
        {
            logger.LogWarning("Attempted to stop unknown operation: {OperationId}", operationId);
            return;
        }

        activeOperation.Stopwatch.Stop();
        RecordTiming(activeOperation.Key, activeOperation.Stopwatch.ElapsedMilliseconds);
    }

    /// <summary>
    /// Tracks an entire operation from start to finish.
    /// </summary>
    /// <param name="operationName">Unique name identifying this operation type.</param>
    /// <param name="action">The action to execute and time.</param>
    public void TrackOperation(string operationName, Action action)
    {
        var stopwatch = Stopwatch.StartNew();
        var operationKey = ResolveOperationKey(operationName);

        try
        {
            action();
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationKey, stopwatch.ElapsedMilliseconds);
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
        var operationKey = ResolveOperationKey(operationName);

        try
        {
            return func();
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationKey, stopwatch.ElapsedMilliseconds);
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
        var operationKey = ResolveOperationKey(operationName);

        try
        {
            await task().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationKey, stopwatch.ElapsedMilliseconds);
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
        var operationKey = ResolveOperationKey(operationName);

        try
        {
            return await task().ConfigureAwait(false);
        }
        finally
        {
            stopwatch.Stop();
            RecordTiming(operationKey, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Gets all metrics for tracked operations.
    /// </summary>
    /// <returns>A dictionary of operation metrics keyed by operation name.</returns>
    public Dictionary<string, OperationMetrics> GetMetrics()
        => BuildMetricsSnapshot(static _ => true);

    /// <summary>
    /// Gets all metrics tracked within a specific scope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <returns>A dictionary of scoped operation metrics keyed by operation name.</returns>
    public Dictionary<string, OperationMetrics> GetMetricsForScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return BuildMetricsSnapshot(key => string.Equals(key.ScopeId, scopeId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Gets a performance report for all tracked operations.
    /// </summary>
    /// <returns>A formatted performance report string.</returns>
    public string GetReport()
        => BuildReport(GetMetrics(), "SYSTEM PERFORMANCE REPORT", "No performance data collected.");

    /// <summary>
    /// Gets a performance report for a specific scope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <returns>A formatted scoped performance report string.</returns>
    public string GetReportForScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return BuildReport(
            GetMetricsForScope(scopeId),
            $"SYSTEM PERFORMANCE REPORT ({scopeId})",
            $"No performance data collected for scope '{scopeId}'.");
    }

    /// <summary>
    /// Logs a performance report to the logger.
    /// </summary>
    public void LogReport()
        => LogMetrics(GetMetrics(), "SYSTEM PERFORMANCE REPORT", "No performance data collected.");

    /// <summary>
    /// Logs a performance report for a specific scope.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    public void LogReportForScope(string scopeId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        LogMetrics(GetMetricsForScope(scopeId), $"SYSTEM PERFORMANCE REPORT ({scopeId})", "No performance data collected.");
    }

    /// <summary>
    /// Saves the performance report to a file.
    /// </summary>
    /// <param name="filePath">Path where the report should be saved. If null, will use a default timestamped path in the current directory.</param>
    /// <returns>The path to the saved file.</returns>
    public string SaveReportToFile(
        string? filePath = null,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
        => SaveReportToFileInternal(
            BuildReport(
                GetMetrics(),
                "SYSTEM PERFORMANCE REPORT",
                "No performance data collected.",
                supplementalMetrics),
            filePath,
            "Performance report saved to: {FilePath}");

    /// <summary>
    /// Saves the performance report for a specific scope to a file.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <param name="filePath">Path where the report should be saved. If null, will use a default timestamped path in the current directory.</param>
    /// <returns>The path to the saved file.</returns>
    public string SaveReportToFileForScope(
        string scopeId,
        string? filePath = null,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return SaveReportToFileInternal(
            BuildReport(
                GetMetricsForScope(scopeId),
                $"SYSTEM PERFORMANCE REPORT ({scopeId})",
                $"No performance data collected for scope '{scopeId}'.",
                supplementalMetrics),
            filePath,
            "Performance report for scope saved to: {FilePath}");
    }

    /// <summary>
    /// Saves the performance report as CSV for further analysis.
    /// </summary>
    /// <param name="filePath">Path where the CSV should be saved. If null, will use a default timestamped path.</param>
    /// <returns>The path to the saved CSV file.</returns>
    public string SaveReportToCsv(
        string? filePath = null,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
        => SaveReportToCsvInternal(
            GetMetrics(),
            filePath,
            "Performance report CSV saved to: {FilePath}",
            supplementalMetrics);

    /// <summary>
    /// Saves the scoped performance report as CSV for further analysis.
    /// </summary>
    /// <param name="scopeId">The scope identifier.</param>
    /// <param name="filePath">Path where the CSV should be saved. If null, will use a default timestamped path.</param>
    /// <returns>The path to the saved CSV file.</returns>
    public string SaveReportToCsvForScope(
        string scopeId,
        string? filePath = null,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scopeId);
        return SaveReportToCsvInternal(
            GetMetricsForScope(scopeId),
            filePath,
            "Performance report CSV for scope saved to: {FilePath}",
            supplementalMetrics);
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
        foreach (var operation in activeOperations)
        {
            operation.Value.Stopwatch.Stop();
            RecordTiming(operation.Value.Key, operation.Value.Stopwatch.ElapsedMilliseconds);
        }

        activeOperations.Clear();
    }

    internal void RestoreScope(ScopeFrame? scopeFrame) => currentScope.Value = scopeFrame;

    private void RecordTiming(OperationKey operationKey, long elapsedMs)
    {
        timings.GetOrAdd(operationKey, _ => new ConcurrentBag<long>()).Add(elapsedMs);
        counts.AddOrUpdate(operationKey, 1, (_, count) => count + 1);
        totalTimes.AddOrUpdate(operationKey, elapsedMs, (_, total) => total + elapsedMs);

        if (elapsedMs >= InformationLogThresholdMs)
        {
            logger.LogInformation("{Operation} completed in {ElapsedMs}ms", operationKey.OperationName, elapsedMs);
        }
        else
        {
            logger.LogDebug("{Operation} completed in {ElapsedMs}ms", operationKey.OperationName, elapsedMs);
        }
    }

    private OperationKey ResolveOperationKey(string operationName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        return new OperationKey(currentScope.Value?.ScopeId, operationName);
    }

    private Dictionary<string, OperationMetrics> BuildMetricsSnapshot(Func<OperationKey, bool> predicate)
    {
        var aggregatedTimings = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        var aggregatedCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var aggregatedTotals = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var entry in timings)
        {
            if (!predicate(entry.Key))
            {
                continue;
            }

            var operationName = entry.Key.OperationName;
            var values = entry.Value.ToArray();
            if (values.Length == 0)
            {
                continue;
            }

            if (!aggregatedTimings.TryGetValue(operationName, out var metricTimings))
            {
                metricTimings = new List<long>(values.Length);
                aggregatedTimings[operationName] = metricTimings;
            }

            metricTimings.AddRange(values);
            aggregatedCounts[operationName] = aggregatedCounts.GetValueOrDefault(operationName) + counts.GetValueOrDefault(entry.Key);
            aggregatedTotals[operationName] = aggregatedTotals.GetValueOrDefault(operationName) + totalTimes.GetValueOrDefault(entry.Key);
        }

        var result = new Dictionary<string, OperationMetrics>(StringComparer.Ordinal);
        foreach (var (operationName, metricTimings) in aggregatedTimings)
        {
            var values = metricTimings.ToArray();
            result[operationName] = new OperationMetrics
            {
                OperationName = operationName,
                CallCount = aggregatedCounts.GetValueOrDefault(operationName),
                TotalTimeMs = aggregatedTotals.GetValueOrDefault(operationName),
                AverageTimeMs = values.Average(),
                MinTimeMs = values.Min(),
                MaxTimeMs = values.Max(),
                MedianTimeMs = CalculateMedian(values),
            };
        }

        return result;
    }

    private string BuildReport(
        Dictionary<string, OperationMetrics> metrics,
        string header,
        string emptyMessage,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
    {
        var report = new System.Text.StringBuilder();
        if (!metrics.Any())
        {
            report.AppendLine(emptyMessage);
        }
        else
        {
            report.AppendLine(header);
            report.AppendLine(new string('=', header.Length));
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
        }

        AppendSupplementalMetricsSection(report, BuildSupplementalMetricsSnapshot(supplementalMetrics));

        return report.ToString();
    }

    private void LogMetrics(Dictionary<string, OperationMetrics> metrics, string header, string emptyMessage)
    {
        if (!metrics.Any())
        {
            logger.LogInformation(emptyMessage);
            return;
        }

        logger.LogInformation(header);

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

    private string SaveReportToFileInternal(string report, string? filePath, string logMessage)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.txt");
        }

        EnsureDirectoryExists(filePath);
        File.WriteAllText(filePath, report);
        logger.LogInformation(logMessage, filePath);
        return filePath;
    }

    private string SaveReportToCsvInternal(
        Dictionary<string, OperationMetrics> metrics,
        string? filePath,
        string logMessage,
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics = null)
    {
        if (string.IsNullOrEmpty(filePath))
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            filePath = Path.Combine(Directory.GetCurrentDirectory(), $"PerformanceReport_{timestamp}.csv");
        }

        EnsureDirectoryExists(filePath);

        using var writer = new StreamWriter(filePath);
        writer.WriteLine("Operation,CallCount,TotalTimeMs,AverageTimeMs,MedianTimeMs,MinTimeMs,MaxTimeMs");

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

        var supplementalMetricRows = BuildSupplementalMetricsSnapshot(supplementalMetrics);
        if (supplementalMetricRows.Count > 0)
        {
            writer.WriteLine();
            writer.WriteLine("Metric,Value");

            foreach (var supplementalMetric in supplementalMetricRows)
            {
                writer.WriteLine(
                    string.Format(
                        CultureInfo.InvariantCulture,
                        "{0},{1}",
                        EscapeCsv(supplementalMetric.Name),
                        EscapeCsv(supplementalMetric.Value)));
            }
        }

        logger.LogInformation(logMessage, filePath);
        return filePath;
    }

    private static void AppendSupplementalMetricsSection(
        System.Text.StringBuilder report,
        IReadOnlyList<SupplementalMetricRow> supplementalMetricRows)
    {
        if (supplementalMetricRows.Count == 0)
        {
            return;
        }

        if (report.Length > 0)
        {
            report.AppendLine();
        }

        report.AppendLine("SUPPLEMENTAL METRICS");
        report.AppendLine("====================");
        report.AppendLine();

        foreach (var supplementalMetric in supplementalMetricRows)
        {
            report.AppendLine($"{supplementalMetric.Name}: {supplementalMetric.Value}");
        }
    }

    private static List<SupplementalMetricRow> BuildSupplementalMetricsSnapshot(
        IEnumerable<KeyValuePair<string, object?>>? supplementalMetrics)
    {
        var supplementalMetricRows = new List<SupplementalMetricRow>();
        if (supplementalMetrics == null)
        {
            return supplementalMetricRows;
        }

        foreach (var supplementalMetric in supplementalMetrics)
        {
            if (string.IsNullOrWhiteSpace(supplementalMetric.Key))
            {
                continue;
            }

            supplementalMetricRows.Add(
                new SupplementalMetricRow(
                    supplementalMetric.Key,
                    FormatSupplementalMetricValue(supplementalMetric.Value)));
        }

        return supplementalMetricRows;
    }

    private static string FormatSupplementalMetricValue(object? value)
        => value switch
        {
            null => string.Empty,
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',', StringComparison.Ordinal)
            && !value.Contains('"')
            && !value.Contains('\r')
            && !value.Contains('\n'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void EnsureDirectoryExists(string filePath)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }

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

public sealed class PerformanceTrackingScope : IDisposable
{
    private readonly PerformanceTracker owner;
    private readonly ScopeFrame? previousScope;
    private bool disposed;

    internal PerformanceTrackingScope(PerformanceTracker owner, string scopeId, ScopeFrame? previousScope)
    {
        this.owner = owner;
        this.previousScope = previousScope;
        ScopeId = scopeId;
    }

    public string ScopeId { get; }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        owner.RestoreScope(previousScope);
    }
}

internal sealed record ActiveOperation(OperationKey Key, Stopwatch Stopwatch);
internal sealed record ScopeFrame(string ScopeId);
internal readonly record struct OperationKey(string? ScopeId, string OperationName);
internal readonly record struct SupplementalMetricRow(string Name, string Value);

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
