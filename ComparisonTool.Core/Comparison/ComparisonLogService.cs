// <copyright file="ComparisonLogService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Text;
using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Dedicated logging service for comparison operations.
/// Writes detailed comparison statistics and errors to a separate log file.
/// </summary>
public interface IComparisonLogService
{
    /// <summary>
    /// Starts a new comparison session and returns a session ID for tracking.
    /// </summary>
    /// <returns></returns>
    string StartSession(string modelName, int totalFilePairs);

    /// <summary>
    /// Logs the result of a single file pair comparison.
    /// </summary>
    void LogFilePairResult(string sessionId, FilePairComparisonResult result);

    /// <summary>
    /// Completes the session and writes a summary.
    /// </summary>
    void EndSession(string sessionId, MultiFolderComparisonResult result);

    /// <summary>
    /// Logs an error that occurred during comparison.
    /// </summary>
    void LogError(string sessionId, string message, Exception? exception = null);

    /// <summary>
    /// Gets the current session statistics.
    /// </summary>
    /// <returns></returns>
    ComparisonSessionStats GetSessionStats(string sessionId);
}

/// <summary>
/// Statistics for a comparison session.
/// </summary>
public class ComparisonSessionStats
{
    public int TotalFilePairs
    {
        get; set;
    }

    public int ProcessedFilePairs
    {
        get; set;
    }

    public int EqualFilePairs
    {
        get; set;
    }

    public int DifferentFilePairs
    {
        get; set;
    }

    public int ErrorFilePairs
    {
        get; set;
    }

    public Dictionary<string, int> ErrorsByType { get; set; } = new Dictionary<string, int>(StringComparer.Ordinal);

    public List<string> FilesWithErrors { get; set; } = new List<string>();

    public DateTime StartTime
    {
        get; set;
    }

    public DateTime? EndTime
    {
        get; set;
    }

    public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
}

/// <summary>
/// Implementation of the comparison log service.
/// </summary>
public class ComparisonLogService : IComparisonLogService, IDisposable
{
    private readonly ILogger<ComparisonLogService> logger;
    private readonly string logDirectory;
    private readonly ConcurrentDictionary<string, ComparisonSessionStats> sessions = new ConcurrentDictionary<string, ComparisonSessionStats>(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, StreamWriter> sessionWriters = new ConcurrentDictionary<string, StreamWriter>(StringComparer.Ordinal);
    private readonly object fileLock = new object();
    private bool disposed;

    public ComparisonLogService(ILogger<ComparisonLogService> logger)
    {
        this.logger = logger;

        // Create a dedicated log directory for comparison logs
        logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Comparisons");
        Directory.CreateDirectory(logDirectory);
    }

    /// <inheritdoc/>
    public string StartSession(string modelName, int totalFilePairs)
    {
        var sessionId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString()[..8]}";
        var stats = new ComparisonSessionStats
        {
            TotalFilePairs = totalFilePairs,
            StartTime = DateTime.UtcNow,
        };

        sessions[sessionId] = stats;

        // Create a dedicated log file for this session
        var logFilePath = Path.Combine(logDirectory, $"comparison_{sessionId}.log");
        var writer = new StreamWriter(logFilePath, append: false, encoding: Encoding.UTF8);
        sessionWriters[sessionId] = writer;

        WriteToSession(sessionId, $"=== COMPARISON SESSION STARTED ===");
        WriteToSession(sessionId, $"Session ID: {sessionId}");
        WriteToSession(sessionId, $"Model: {modelName}");
        WriteToSession(sessionId, $"Total File Pairs: {totalFilePairs}");
        WriteToSession(sessionId, $"Start Time: {stats.StartTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
        WriteToSession(sessionId, new string('=', 60));
        WriteToSession(sessionId, string.Empty);

        logger.LogInformation(
            "Comparison session {SessionId} started for model {ModelName} with {TotalPairs} file pairs",
            sessionId,
            modelName,
            totalFilePairs);

        return sessionId;
    }

    /// <inheritdoc/>
    public void LogFilePairResult(string sessionId, FilePairComparisonResult result)
    {
        if (!sessions.TryGetValue(sessionId, out var stats))
        {
            return;
        }

        stats.ProcessedFilePairs++;

        if (result.HasError)
        {
            stats.ErrorFilePairs++;
            stats.FilesWithErrors.Add($"{result.File1Name} vs {result.File2Name}");

            // Track errors by type
            var errorType = result.ErrorType ?? "Unknown";
            if (!stats.ErrorsByType.ContainsKey(errorType))
            {
                stats.ErrorsByType[errorType] = 0;
            }

            stats.ErrorsByType[errorType]++;

            WriteToSession(sessionId, $"[ERROR] {result.File1Name} vs {result.File2Name}");
            WriteToSession(sessionId, $"  Error Type: {result.ErrorType}");
            WriteToSession(sessionId, $"  Message: {result.ErrorMessage}");
            WriteToSession(sessionId, string.Empty);

            logger.LogWarning(
                "Session {SessionId}: File comparison error - {File1} vs {File2}: [{ErrorType}] {ErrorMessage}",
                sessionId,
                result.File1Name,
                result.File2Name,
                result.ErrorType,
                result.ErrorMessage);
        }
        else if (result.AreEqual)
        {
            stats.EqualFilePairs++;
        }
        else
        {
            stats.DifferentFilePairs++;

            var differenceCount = result.Summary?.TotalDifferenceCount ?? result.Result?.Differences?.Count ?? 0;
            WriteToSession(sessionId, $"[DIFFERENT] {result.File1Name} vs {result.File2Name} - {differenceCount} differences");
        }
    }

    /// <inheritdoc/>
    public void EndSession(string sessionId, MultiFolderComparisonResult result)
    {
        if (!sessions.TryGetValue(sessionId, out var stats))
        {
            return;
        }

        stats.EndTime = DateTime.UtcNow;

        WriteSessionSummary(sessionId, stats);
        CloseSessionWriter(sessionId);

        logger.LogInformation(
            "Comparison session {SessionId} completed: {Processed} processed, {Equal} equal, {Different} different, {Errors} errors (Duration: {Duration:F2}s)",
            sessionId,
            stats.ProcessedFilePairs,
            stats.EqualFilePairs,
            stats.DifferentFilePairs,
            stats.ErrorFilePairs,
            stats.Duration.TotalSeconds);

        if (stats.ErrorFilePairs > 0)
        {
            logger.LogWarning(
                "Session {SessionId} had {ErrorCount} file pairs with errors. Error types: {ErrorTypes}",
                sessionId,
                stats.ErrorFilePairs,
                string.Join(", ", stats.ErrorsByType.Select(k => $"{k.Key}:{k.Value}")));
        }
    }

    /// <inheritdoc/>
    public void LogError(string sessionId, string message, Exception? exception = null)
    {
        WriteToSession(sessionId, $"[ERROR] {message}");
        if (exception != null)
        {
            WriteToSession(sessionId, $"  Exception: {exception.GetType().Name}");
            WriteToSession(sessionId, $"  Message: {exception.Message}");
            if (exception.StackTrace != null)
            {
                WriteToSession(sessionId, $"  Stack Trace: {exception.StackTrace}");
            }
        }

        logger.LogError(exception, "Session {SessionId}: {Message}", sessionId, message);
    }

    /// <inheritdoc/>
    public ComparisonSessionStats GetSessionStats(string sessionId)
    {
        return sessions.TryGetValue(sessionId, out var stats) ? stats : new ComparisonSessionStats();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        foreach (var writer in sessionWriters.Values)
        {
            try
            {
                writer.Flush();
                writer.Close();
                writer.Dispose();
            }
            catch
            {
                // Ignore errors during disposal
            }
        }

        sessionWriters.Clear();
        sessions.Clear();
    }

    private void WriteSessionSummary(string sessionId, ComparisonSessionStats stats)
    {
        WriteToSession(sessionId, string.Empty);
        WriteToSession(sessionId, new string('=', 60));
        WriteToSession(sessionId, "=== COMPARISON SESSION SUMMARY ===");
        WriteToSession(sessionId, $"Session ID: {sessionId}");
        WriteToSession(sessionId, $"End Time: {stats.EndTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
        WriteToSession(sessionId, $"Duration: {stats.Duration.TotalSeconds:F2} seconds");
        WriteToSession(sessionId, string.Empty);
        WriteToSession(sessionId, "--- Statistics ---");
        WriteToSession(sessionId, $"Total File Pairs: {stats.TotalFilePairs}");
        WriteToSession(sessionId, $"Processed: {stats.ProcessedFilePairs}");
        WriteToSession(sessionId, $"Equal: {stats.EqualFilePairs}");
        WriteToSession(sessionId, $"Different: {stats.DifferentFilePairs}");
        WriteToSession(sessionId, $"Errors: {stats.ErrorFilePairs}");
        WriteToSession(sessionId, string.Empty);

        if (stats.ErrorFilePairs > 0)
        {
            WriteErrorSummary(sessionId, stats);
        }

        WriteToSession(sessionId, string.Empty);
        WriteToSession(sessionId, "=== END OF SESSION ===");
    }

    private void WriteErrorSummary(string sessionId, ComparisonSessionStats stats)
    {
        WriteToSession(sessionId, "--- Errors by Type ---");
        foreach (var kvp in stats.ErrorsByType.OrderByDescending(k => k.Value))
        {
            WriteToSession(sessionId, $"  {kvp.Key}: {kvp.Value}");
        }

        WriteToSession(sessionId, string.Empty);
        WriteToSession(sessionId, "--- Files with Errors ---");
        foreach (var file in stats.FilesWithErrors.Take(100))
        {
            WriteToSession(sessionId, $"  {file}");
        }

        if (stats.FilesWithErrors.Count > 100)
        {
            WriteToSession(sessionId, $"  ... and {stats.FilesWithErrors.Count - 100} more");
        }
    }

    private void CloseSessionWriter(string sessionId)
    {
        if (sessionWriters.TryRemove(sessionId, out var writer))
        {
            writer.Flush();
            writer.Close();
            writer.Dispose();
        }
    }

    private void WriteToSession(string sessionId, string message)
    {
        if (sessionWriters.TryGetValue(sessionId, out var writer))
        {
            lock (fileLock)
            {
                writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}");
            }
        }
    }
}
