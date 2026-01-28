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
public interface IComparisonLogService {
    /// <summary>
    /// Starts a new comparison session and returns a session ID for tracking.
    /// </summary>
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
    ComparisonSessionStats GetSessionStats(string sessionId);
}

/// <summary>
/// Statistics for a comparison session.
/// </summary>
public class ComparisonSessionStats {
    public int TotalFilePairs { get; set; }

    public int ProcessedFilePairs { get; set; }

    public int EqualFilePairs { get; set; }

    public int DifferentFilePairs { get; set; }

    public int ErrorFilePairs { get; set; }

    public Dictionary<string, int> ErrorsByType { get; set; } = new();

    public List<string> FilesWithErrors { get; set; } = new();

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public TimeSpan Duration => (this.EndTime ?? DateTime.UtcNow) - this.StartTime;
}

/// <summary>
/// Implementation of the comparison log service.
/// </summary>
public class ComparisonLogService : IComparisonLogService, IDisposable {
    private readonly ILogger<ComparisonLogService> logger;
    private readonly string logDirectory;
    private readonly ConcurrentDictionary<string, ComparisonSessionStats> sessions = new();
    private readonly ConcurrentDictionary<string, StreamWriter> sessionWriters = new();
    private readonly object fileLock = new();
    private bool disposed;

    public ComparisonLogService(ILogger<ComparisonLogService> logger) {
        this.logger = logger;

        // Create a dedicated log directory for comparison logs
        this.logDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Comparisons");
        Directory.CreateDirectory(this.logDirectory);
    }

    /// <inheritdoc/>
    public string StartSession(string modelName, int totalFilePairs) {
        var sessionId = $"{DateTime.Now:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString()[..8]}";
        var stats = new ComparisonSessionStats {
            TotalFilePairs = totalFilePairs,
            StartTime = DateTime.UtcNow,
        };

        this.sessions[sessionId] = stats;

        // Create a dedicated log file for this session
        var logFilePath = Path.Combine(this.logDirectory, $"comparison_{sessionId}.log");
        var writer = new StreamWriter(logFilePath, append: false, encoding: Encoding.UTF8);
        this.sessionWriters[sessionId] = writer;

        this.WriteToSession(sessionId, $"=== COMPARISON SESSION STARTED ===");
        this.WriteToSession(sessionId, $"Session ID: {sessionId}");
        this.WriteToSession(sessionId, $"Model: {modelName}");
        this.WriteToSession(sessionId, $"Total File Pairs: {totalFilePairs}");
        this.WriteToSession(sessionId, $"Start Time: {stats.StartTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
        this.WriteToSession(sessionId, new string('=', 60));
        this.WriteToSession(sessionId, string.Empty);

        this.logger.LogInformation(
            "Comparison session {SessionId} started for model {ModelName} with {TotalPairs} file pairs",
            sessionId, modelName, totalFilePairs);

        return sessionId;
    }

    /// <inheritdoc/>
    public void LogFilePairResult(string sessionId, FilePairComparisonResult result) {
        if (!this.sessions.TryGetValue(sessionId, out var stats)) {
            return;
        }

        stats.ProcessedFilePairs++;

        if (result.HasError) {
            stats.ErrorFilePairs++;
            stats.FilesWithErrors.Add($"{result.File1Name} vs {result.File2Name}");

            // Track errors by type
            var errorType = result.ErrorType ?? "Unknown";
            if (!stats.ErrorsByType.ContainsKey(errorType)) {
                stats.ErrorsByType[errorType] = 0;
            }

            stats.ErrorsByType[errorType]++;

            this.WriteToSession(sessionId, $"[ERROR] {result.File1Name} vs {result.File2Name}");
            this.WriteToSession(sessionId, $"  Error Type: {result.ErrorType}");
            this.WriteToSession(sessionId, $"  Message: {result.ErrorMessage}");
            this.WriteToSession(sessionId, string.Empty);

            this.logger.LogWarning(
                "Session {SessionId}: File comparison error - {File1} vs {File2}: [{ErrorType}] {ErrorMessage}",
                sessionId, result.File1Name, result.File2Name, result.ErrorType, result.ErrorMessage);
        }
        else if (result.AreEqual) {
            stats.EqualFilePairs++;
        }
        else {
            stats.DifferentFilePairs++;

            var differenceCount = result.Summary?.TotalDifferenceCount ?? result.Result?.Differences?.Count ?? 0;
            this.WriteToSession(sessionId, $"[DIFFERENT] {result.File1Name} vs {result.File2Name} - {differenceCount} differences");
        }
    }

    /// <inheritdoc/>
    public void EndSession(string sessionId, MultiFolderComparisonResult result) {
        if (!this.sessions.TryGetValue(sessionId, out var stats)) {
            return;
        }

        stats.EndTime = DateTime.UtcNow;

        this.WriteToSession(sessionId, string.Empty);
        this.WriteToSession(sessionId, new string('=', 60));
        this.WriteToSession(sessionId, "=== COMPARISON SESSION SUMMARY ===");
        this.WriteToSession(sessionId, $"Session ID: {sessionId}");
        this.WriteToSession(sessionId, $"End Time: {stats.EndTime:yyyy-MM-dd HH:mm:ss.fff} UTC");
        this.WriteToSession(sessionId, $"Duration: {stats.Duration.TotalSeconds:F2} seconds");
        this.WriteToSession(sessionId, string.Empty);
        this.WriteToSession(sessionId, "--- Statistics ---");
        this.WriteToSession(sessionId, $"Total File Pairs: {stats.TotalFilePairs}");
        this.WriteToSession(sessionId, $"Processed: {stats.ProcessedFilePairs}");
        this.WriteToSession(sessionId, $"Equal: {stats.EqualFilePairs}");
        this.WriteToSession(sessionId, $"Different: {stats.DifferentFilePairs}");
        this.WriteToSession(sessionId, $"Errors: {stats.ErrorFilePairs}");
        this.WriteToSession(sessionId, string.Empty);

        if (stats.ErrorFilePairs > 0) {
            this.WriteToSession(sessionId, "--- Errors by Type ---");
            foreach (var kvp in stats.ErrorsByType.OrderByDescending(k => k.Value)) {
                this.WriteToSession(sessionId, $"  {kvp.Key}: {kvp.Value}");
            }

            this.WriteToSession(sessionId, string.Empty);
            this.WriteToSession(sessionId, "--- Files with Errors ---");
            foreach (var file in stats.FilesWithErrors.Take(100)) {
                this.WriteToSession(sessionId, $"  {file}");
            }

            if (stats.FilesWithErrors.Count > 100) {
                this.WriteToSession(sessionId, $"  ... and {stats.FilesWithErrors.Count - 100} more");
            }
        }

        this.WriteToSession(sessionId, string.Empty);
        this.WriteToSession(sessionId, "=== END OF SESSION ===");

        // Flush and close the writer
        if (this.sessionWriters.TryRemove(sessionId, out var writer)) {
            writer.Flush();
            writer.Close();
            writer.Dispose();
        }

        this.logger.LogInformation(
            "Comparison session {SessionId} completed: {Processed} processed, {Equal} equal, {Different} different, {Errors} errors (Duration: {Duration:F2}s)",
            sessionId, stats.ProcessedFilePairs, stats.EqualFilePairs, stats.DifferentFilePairs, stats.ErrorFilePairs, stats.Duration.TotalSeconds);

        if (stats.ErrorFilePairs > 0) {
            this.logger.LogWarning(
                "Session {SessionId} had {ErrorCount} file pairs with errors. Error types: {ErrorTypes}",
                sessionId, stats.ErrorFilePairs, string.Join(", ", stats.ErrorsByType.Select(k => $"{k.Key}:{k.Value}")));
        }
    }

    /// <inheritdoc/>
    public void LogError(string sessionId, string message, Exception? exception = null) {
        this.WriteToSession(sessionId, $"[ERROR] {message}");
        if (exception != null) {
            this.WriteToSession(sessionId, $"  Exception: {exception.GetType().Name}");
            this.WriteToSession(sessionId, $"  Message: {exception.Message}");
            if (exception.StackTrace != null) {
                this.WriteToSession(sessionId, $"  Stack Trace: {exception.StackTrace}");
            }
        }

        this.logger.LogError(exception, "Session {SessionId}: {Message}", sessionId, message);
    }

    /// <inheritdoc/>
    public ComparisonSessionStats GetSessionStats(string sessionId) {
        return this.sessions.TryGetValue(sessionId, out var stats) ? stats : new ComparisonSessionStats();
    }

    private void WriteToSession(string sessionId, string message) {
        if (this.sessionWriters.TryGetValue(sessionId, out var writer)) {
            lock (this.fileLock) {
                writer.WriteLine($"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {message}");
            }
        }
    }

    /// <inheritdoc/>
    public void Dispose() {
        if (this.disposed) {
            return;
        }

        this.disposed = true;

        foreach (var writer in this.sessionWriters.Values) {
            try {
                writer.Flush();
                writer.Close();
                writer.Dispose();
            }
            catch {
                // Ignore errors during disposal
            }
        }

        this.sessionWriters.Clear();
        this.sessions.Clear();
    }
}
