using System.Collections.Concurrent;
using ComparisonTool.Core.Comparison;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Web.Services;

/// <summary>
/// Service that handles directory-based XML comparisons to efficiently process large sets of files.
/// </summary>
public class DirectoryComparisonService
{
    private readonly ILogger<DirectoryComparisonService> logger;
    private readonly IComparisonService comparisonService;
    private readonly IFileSystemService fileSystemService;
    private readonly IXmlDeserializationService deserializationService;
    private readonly IComparisonConfigurationService configService;
    private readonly PerformanceTracker performanceTracker;
    private readonly SystemResourceMonitor resourceMonitor;
    private readonly IComparisonLogService comparisonLogService;

    public DirectoryComparisonService(
        ILogger<DirectoryComparisonService> logger,
        IComparisonService comparisonService,
        IFileSystemService fileSystemService,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        PerformanceTracker performanceTracker,
        SystemResourceMonitor resourceMonitor,
        IComparisonLogService comparisonLogService)
    {
        this.logger = logger;
        this.comparisonService = comparisonService;
        this.fileSystemService = fileSystemService;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.performanceTracker = performanceTracker;
        this.resourceMonitor = resourceMonitor;
        this.comparisonLogService = comparisonLogService;
    }

    /// <summary>
    /// Compare XML files from two directories.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<MultiFolderComparisonResult> CompareDirectoriesAsync(
        string directory1Path,
        string directory2Path,
        string modelName,
        bool enablePatternAnalysis = true,
        bool enableSemanticAnalysis = true,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            throw new ArgumentException("Model name must be specified", nameof(modelName));
        }

        string sessionId = null;

        try
        {
            // Apply configuration
            configService.ApplyConfiguredSettings();

            // Find matching file pairs
            progress?.Report(new ComparisonProgress(0, 0, "Finding matching files..."));

            var filePairs = await fileSystemService.CreateFilePairsAsync(
                directory1Path,
                directory2Path,
                cancellationToken).ConfigureAwait(false);

            if (filePairs.Count == 0)
            {
                logger.LogWarning(
                    "No matching file pairs found in directories {Dir1} and {Dir2}",
                    directory1Path,
                    directory2Path);

                return new MultiFolderComparisonResult
                {
                    AllEqual = true,
                    TotalPairsCompared = 0,
                    FilePairResults = new List<FilePairComparisonResult>(),
                    Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
                };
            }

            // Start comparison logging session
            sessionId = comparisonLogService.StartSession(modelName, filePairs.Count);

            logger.LogInformation("Found {Count} matching file pairs to compare", filePairs.Count);
            progress?.Report(new ComparisonProgress(0, filePairs.Count, $"Found {filePairs.Count} matching files"));

            // Compare files in batches using streams instead of loading all into memory
            var result = new MultiFolderComparisonResult
            {
                TotalPairsCompared = filePairs.Count,
                AllEqual = true,
                Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
            };

            // Store session ID in metadata for later retrieval
            result.Metadata["ComparisonSessionId"] = sessionId;

            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
            var completedPairs = 0;
            var equalityFlag = 1; // Start with all equal (1=equal, 0=different)

            // Determine batch size based on file count
            var batchSize = CalculateBatchSize(filePairs.Count);
            var batches = (filePairs.Count + batchSize - 1) / batchSize;

            // Process each batch
            for (var batchIndex = 0; batchIndex < batches; batchIndex++)
            {
                var batchFilePairs = filePairs
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToList();

                logger.LogInformation(
                    "Processing batch {Current}/{Total} with {Count} file pairs",
                    batchIndex + 1,
                    batches,
                    batchFilePairs.Count);

                progress?.Report(new ComparisonProgress(
                    completedPairs,
                    filePairs.Count,
                    $"Processing batch {batchIndex + 1} of {batches}"));

                // Process this batch in parallel
                await Parallel.ForEachAsync(
                    batchFilePairs,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = CalculateParallelism(batchFilePairs.Count),
                        CancellationToken = cancellationToken,
                    },
                    async (filePair, ct) =>
                    {
                        var (file1Path, file2Path, relativePath) = filePair;
                        try
                        {
                            // Open file streams without loading entirely into memory
                            using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct).ConfigureAwait(false);
                            using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct).ConfigureAwait(false);

                            // Perform comparison using format-agnostic method
                            var comparisonResult = await comparisonService.CompareFilesWithCachingAsync(
                                file1Stream,
                                file2Stream,
                                modelName,
                                file1Path,
                                file2Path,
                                ct).ConfigureAwait(false);

                            // Generate summary
                            var categorizer = new DifferenceCategorizer();
                            var summary = categorizer.CategorizeAndSummarize(comparisonResult);

                            // Create result
                            var pairResult = new FilePairComparisonResult
                            {
                                File1Name = Path.GetFileName(relativePath),
                                File2Name = Path.GetFileName(relativePath),
                                Result = comparisonResult,
                                Summary = summary,
                            };

                            // Update result
                            filePairResults.Add(pairResult);

                            // Log the result to dedicated comparison log
                            comparisonLogService.LogFilePairResult(sessionId, pairResult);

                            // Update equality flag in a thread-safe way
                            if (!summary.AreEqual)
                            {
                                Interlocked.Exchange(ref equalityFlag, 0);
                            }

                            // Update progress - throttle to reduce UI contention
                            var completed = Interlocked.Increment(ref completedPairs);
                            if (completed % Math.Max(1, filePairs.Count / 50) == 0 || completed == filePairs.Count)
                            {
                                progress?.Report(new ComparisonProgress(
                                    completed,
                                    filePairs.Count,
                                    $"Compared {completed} of {filePairs.Count} files"));
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error comparing file pair {Path}: {Message}", relativePath, ex.Message);

                            // CRITICAL FIX: Create an error result instead of silently skipping
                            // This ensures files with errors (FileNotFound, deserialization failures, etc.)
                            // are NOT counted as "Equal" and are properly reported to the user
                            var errorResult = new FilePairComparisonResult
                            {
                                File1Name = Path.GetFileName(relativePath),
                                File2Name = Path.GetFileName(relativePath),
                                ErrorMessage = ex.Message,
                                ErrorType = ex.GetType().Name,
                            };

                            filePairResults.Add(errorResult);

                            // Log the error result to dedicated comparison log
                            comparisonLogService.LogFilePairResult(sessionId, errorResult);

                            // Mark as not equal since we couldn't determine the result
                            Interlocked.Exchange(ref equalityFlag, 0);

                            // Update progress even for errors
                            var completed = Interlocked.Increment(ref completedPairs);
                            if (completed % Math.Max(1, filePairs.Count / 50) == 0 || completed == filePairs.Count)
                            {
                                progress?.Report(new ComparisonProgress(
                                    completed,
                                    filePairs.Count,
                                    $"Compared {completed} of {filePairs.Count} files (with errors)"));
                            }
                        }
                    }).ConfigureAwait(false);

                // PERFORMANCE: Let GC work naturally instead of forcing collection
                // Only hint GC between batches if memory pressure is high
                if (GC.GetTotalMemory(false) > 500 * 1024 * 1024)
                { // > 500MB
                    GC.Collect(1, GCCollectionMode.Optimized, false);
                }
            }

            // Convert from ConcurrentBag to List and sort
            result.FilePairResults = filePairResults
                .OrderBy(r => r.File1Name, StringComparer.Ordinal)
                .ToList();

            result.AllEqual = equalityFlag == 1; // Convert from int flag to bool

            var equal = result.FilePairResults.Count(r => r.Summary?.AreEqual ?? true);
            var different = result.FilePairResults.Count(r => !(r.Summary?.AreEqual ?? true));
            logger.LogInformation(
                "Directory comparison completed. {Equal} equal, {Different} different",
                equal,
                different);

            progress?.Report(new ComparisonProgress(
                filePairs.Count,
                filePairs.Count,
                "Comparison completed"));

            // Perform pattern analysis if requested
            ComparisonPatternAnalysis patternAnalysis = null;
            if (enablePatternAnalysis && !result.AllEqual && result.FilePairResults.Count > 1)
            {
                progress?.Report(new ComparisonProgress(
                    filePairs.Count,
                    filePairs.Count,
                    "Analyzing patterns..."));

                patternAnalysis = await comparisonService.AnalyzePatternsAsync(result, cancellationToken).ConfigureAwait(false);

                // Perform semantic analysis if requested
                if (enableSemanticAnalysis && patternAnalysis != null)
                {
                    progress?.Report(new ComparisonProgress(
                        filePairs.Count,
                        filePairs.Count,
                        "Analyzing semantic differences..."));

                    var semanticAnalysis = await comparisonService.AnalyzeSemanticDifferencesAsync(
                        result,
                        patternAnalysis,
                        cancellationToken).ConfigureAwait(false);

                    // Store semantic analysis in result metadata
                    result.Metadata["SemanticAnalysis"] = semanticAnalysis;
                }

                // Store pattern analysis in result metadata
                result.Metadata["PatternAnalysis"] = patternAnalysis;
            }

            // End the logging session with final results
            if (sessionId != null)
            {
                comparisonLogService.EndSession(sessionId, result);
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error comparing directories {Dir1} and {Dir2}",
                directory1Path,
                directory2Path);

            // Log error to session if active
            if (sessionId != null)
            {
                comparisonLogService.LogError(sessionId, $"Fatal error comparing directories: {ex.Message}", ex);
            }

            throw;
        }
    }

    /// <summary>
    /// Compare uploaded folders from browser.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<MultiFolderComparisonResult> CompareFolderUploadsAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        bool enablePatternAnalysis = true,
        bool enableSemanticAnalysis = true,
        IProgress<ComparisonProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        await performanceTracker.TrackOperationAsync("CompareFolderUploadsAsync", async () =>
        {
            // Record timing statistics about input
            performanceTracker.TrackOperation("Folder_Stats", () =>
            {
                logger.LogInformation(
                    "Starting comparison of {Folder1Count} files in folder 1 and {Folder2Count} files in folder 2",
                    folder1Files.Count,
                    folder2Files.Count);
            });

            // Sort files by name for consistent ordering
            var sortedFiles = performanceTracker.TrackOperation("Sort_Files", () =>
            {
                var f1 = folder1Files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
                var f2 = folder2Files.OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal).ToList();
                return (f1, f2);
            });

            folder1Files = sortedFiles.f1;
            folder2Files = sortedFiles.f2;

            // Validate that files exist before starting comparison
            // This catches issues with temp file cleanup or invalid paths early
            var missingFiles = new List<string>();
            foreach (var file in folder1Files.Take(10))
            { // Check first 10 as a sample
                if (!File.Exists(file))
                {
                    missingFiles.Add($"Folder1: {file}");
                }
            }

            foreach (var file in folder2Files.Take(10))
            { // Check first 10 as a sample
                if (!File.Exists(file))
                {
                    missingFiles.Add($"Folder2: {file}");
                }
            }

            if (missingFiles.Count > 0)
            {
                logger.LogWarning(
                    "File validation failed. {Count} sample files not found. This may indicate temp files were cleaned up. Missing: {Files}",
                    missingFiles.Count,
                    string.Join(", ", missingFiles.Take(5)));
            }

            // Determine how many pairs we can make (minimum of both lists)
            var pairCount = Math.Min(folder1Files.Count, folder2Files.Count);

            // Start comparison logging session
            var sessionId = comparisonLogService.StartSession(modelName, pairCount);

            progress?.Report(new ComparisonProgress(0, pairCount, $"Will compare {pairCount} files in order"));

            var result = new MultiFolderComparisonResult
            {
                TotalPairsCompared = pairCount,
                AllEqual = true,
                FilePairResults = new List<FilePairComparisonResult>(),
                Metadata = new Dictionary<string, object>(StringComparer.Ordinal) { ["ComparisonSessionId"] = sessionId },
            };

            var equalityFlag = 1;
            var processed = 0;
            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();

            // Process files by index position instead of matching names
            await performanceTracker.TrackOperationAsync("Parallel_Comparison", async () =>
            {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, pairCount),
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = CalculateParallelism(pairCount, folder1Files),
                        CancellationToken = cancellationToken,
                    },
                    async (index, ct) =>
                    {
                        var file1Path = folder1Files[index];
                        var file2Path = folder2Files[index];
                        var file1Name = Path.GetFileName(file1Path);
                        var file2Name = Path.GetFileName(file2Path);

                        try
                        {
                            var operationId = performanceTracker.StartOperation($"Compare_File_{index}");

                            try
                            {
                                using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct).ConfigureAwait(false);
                                using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct).ConfigureAwait(false);

                                var comparisonResult = await comparisonService.CompareFilesWithCachingAsync(
                                    file1Stream,
                                    file2Stream,
                                    modelName,
                                    file1Path,
                                    file2Path,
                                    ct).ConfigureAwait(false);
                                var categorizer = new DifferenceCategorizer();
                                var summary = categorizer.CategorizeAndSummarize(comparisonResult);

                                var pairResult = new FilePairComparisonResult
                                {
                                    File1Name = file1Name,
                                    File2Name = file2Name,
                                    Result = comparisonResult,
                                    Summary = summary,
                                };

                                filePairResults.Add(pairResult);

                                // Log result to dedicated comparison log
                                comparisonLogService.LogFilePairResult(sessionId, pairResult);

                                if (!summary.AreEqual)
                                {
                                    Interlocked.Exchange(ref equalityFlag, 0);
                                }
                            }
                            finally
                            {
                                performanceTracker.StopOperation(operationId);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(
                                ex,
                                "Error comparing file pair at index {Index}: {File1} vs {File2}: {Message}",
                                index,
                                file1Name,
                                file2Name,
                                ex.Message);

                            // CRITICAL FIX: Create an error result instead of silently skipping
                            var errorResult = new FilePairComparisonResult
                            {
                                File1Name = file1Name,
                                File2Name = file2Name,
                                ErrorMessage = ex.Message,
                                ErrorType = ex.GetType().Name,
                            };

                            filePairResults.Add(errorResult);

                            // Log error result to dedicated comparison log
                            comparisonLogService.LogFilePairResult(sessionId, errorResult);

                            // Mark as not equal since we couldn't determine the result
                            Interlocked.Exchange(ref equalityFlag, 0);
                        }

                        var current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0 || current == pairCount)
                        {
                            progress?.Report(new ComparisonProgress(current, pairCount, $"Compared {current} of {pairCount} files"));
                        }
                    }).ConfigureAwait(false);
            }).ConfigureAwait(false);

            // Process results
            await performanceTracker.TrackOperationAsync("Process_Results", () =>
            {
                result.FilePairResults = filePairResults.OrderBy(r => r.File1Name, StringComparer.Ordinal).ToList();
                result.AllEqual = equalityFlag == 1;

                var equal = result.FilePairResults.Count(r => r.Summary?.AreEqual ?? true);
                var different = result.FilePairResults.Count(r => !(r.Summary?.AreEqual ?? true));
                logger.LogInformation(
                    "Comparison completed. {Equal} equal, {Different} different",
                    equal,
                    different);

                progress?.Report(new ComparisonProgress(pairCount, pairCount, "Comparison completed"));
                return Task.CompletedTask;
            }).ConfigureAwait(false);

            // Pattern and semantic analysis
            if (enablePatternAnalysis && !result.AllEqual && result.FilePairResults.Count > 1)
            {
                await performanceTracker.TrackOperationAsync("Pattern_Analysis", async () =>
                {
                    progress?.Report(new ComparisonProgress(pairCount, pairCount, "Analyzing patterns..."));
                    var patternAnalysis = await comparisonService.AnalyzePatternsAsync(result, cancellationToken).ConfigureAwait(false);
                    result.Metadata["PatternAnalysis"] = patternAnalysis;

                    if (enableSemanticAnalysis && patternAnalysis != null)
                    {
                        await performanceTracker.TrackOperationAsync("Semantic_Analysis", async () =>
                        {
                            progress?.Report(new ComparisonProgress(pairCount, pairCount, "Analyzing semantic differences..."));
                            var semanticAnalysis = await comparisonService.AnalyzeSemanticDifferencesAsync(
                                result, patternAnalysis, cancellationToken).ConfigureAwait(false);
                            result.Metadata["SemanticAnalysis"] = semanticAnalysis;
                        }).ConfigureAwait(false);
                    }
                }).ConfigureAwait(false);
            }

            // Log performance data
            performanceTracker.LogReport();

            // Save performance reports to files
            var reportsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "PerformanceReports");
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseFileName = $"FolderComparison_{timestamp}";
            performanceTracker.SaveReportToFile(Path.Combine(reportsDirectory, $"{baseFileName}.txt"));
            performanceTracker.SaveReportToCsv(Path.Combine(reportsDirectory, $"{baseFileName}.csv"));

            // End the logging session with final results
            comparisonLogService.EndSession(sessionId, result);

            return result;
        }).ConfigureAwait(false);

    /// <summary>
    /// Calculate batch size based on file count.
    /// </summary>
    private int CalculateBatchSize(int fileCount)
    {
        // For very large file sets, use smaller batches
        if (fileCount > 2000)
        {
            return 20;
        }

        // For large file sets, use moderate batches
        if (fileCount > 500)
        {
            return 50;
        }

        // For smaller file sets, use larger batches
        if (fileCount > 100)
        {
            return 100;
        }

        // For very small sets, process all at once
        return fileCount;
    }

    /// <summary>
    /// Calculate optimal parallelism based on file count, file sizes, and system resources.
    /// </summary>
    private int CalculateParallelism(int fileCount, IEnumerable<string>? sampleFiles = null)
    {
        // Use the resource monitor to determine optimal parallelism
        long averageFileSizeKb = 0;

        // If sample files were provided, estimate average file size
        if (sampleFiles != null)
        {
            averageFileSizeKb = performanceTracker.TrackOperation("Calculate_Avg_FileSize", () =>
                resourceMonitor.CalculateAverageFileSizeKb(sampleFiles.Take(Math.Min(20, fileCount))));
        }

        return resourceMonitor.CalculateOptimalParallelism(fileCount, averageFileSizeKb);
    }
}
