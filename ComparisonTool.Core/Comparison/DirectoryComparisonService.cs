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
/// Service that handles directory-based XML comparisons to efficiently process large sets of files
/// </summary>
public class DirectoryComparisonService
{
    private readonly ILogger<DirectoryComparisonService> logger;
    private readonly IComparisonService comparisonService;
    private readonly IFileSystemService fileSystemService;
    private readonly IXmlDeserializationService deserializationService;
    private readonly IComparisonConfigurationService configService;
    private readonly PerformanceTracker _performanceTracker;
    private readonly SystemResourceMonitor _resourceMonitor;

    public DirectoryComparisonService(
        ILogger<DirectoryComparisonService> logger,
        IComparisonService comparisonService,
        IFileSystemService fileSystemService,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        PerformanceTracker performanceTracker,
        SystemResourceMonitor resourceMonitor)
    {
        this.logger = logger;
        this.comparisonService = comparisonService;
        this.fileSystemService = fileSystemService;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this._performanceTracker = performanceTracker;
        this._resourceMonitor = resourceMonitor;
    }

    /// <summary>
    /// Compare XML files from two directories
    /// </summary>
    public async Task<MultiFolderComparisonResult> CompareDirectoriesAsync(
        string directory1Path,
        string directory2Path,
        string modelName,
        bool enablePatternAnalysis = true,
        bool enableSemanticAnalysis = true,
        IProgress<ComparisonProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(modelName))
        {
            throw new ArgumentException("Model name must be specified", nameof(modelName));
        }

        try
        {
            // Apply configuration
            configService.ApplyConfiguredSettings();

            // Find matching file pairs
            progress?.Report(new ComparisonProgress(0, 0, "Finding matching files..."));

            var filePairs = await fileSystemService.CreateFilePairsAsync(
                directory1Path,
                directory2Path,
                cancellationToken);

            if (filePairs.Count == 0)
            {
                logger.LogWarning("No matching file pairs found in directories {Dir1} and {Dir2}",
                    directory1Path, directory2Path);

                return new MultiFolderComparisonResult
                {
                    AllEqual = true,
                    TotalPairsCompared = 0,
                    FilePairResults = new List<FilePairComparisonResult>(),
                    Metadata = new Dictionary<string, object>()
                };
            }

            logger.LogInformation("Found {Count} matching file pairs to compare", filePairs.Count);
            progress?.Report(new ComparisonProgress(0, filePairs.Count, $"Found {filePairs.Count} matching files"));

            // Compare files in batches using streams instead of loading all into memory
            var result = new MultiFolderComparisonResult
            {
                TotalPairsCompared = filePairs.Count,
                AllEqual = true,
                Metadata = new Dictionary<string, object>()
            };

            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
            int completedPairs = 0;
            int equalityFlag = 1; // Start with all equal (1=equal, 0=different)

            // Determine batch size based on file count
            int batchSize = CalculateBatchSize(filePairs.Count);
            int batches = (filePairs.Count + batchSize - 1) / batchSize;

            // Process each batch
            for (int batchIndex = 0; batchIndex < batches; batchIndex++)
            {
                var batchFilePairs = filePairs
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToList();

                logger.LogInformation("Processing batch {Current}/{Total} with {Count} file pairs",
                    batchIndex + 1, batches, batchFilePairs.Count);

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
                        CancellationToken = cancellationToken
                    },
                    async (filePair, ct) =>
                    {
                        try
                        {
                            var (file1Path, file2Path, relativePath) = filePair;

                            // Open file streams without loading entirely into memory
                            using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct);
                            using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct);

                            // Perform comparison
                            var comparisonResult = await comparisonService.CompareXmlFilesAsync(
                                file1Stream,
                                file2Stream,
                                modelName,
                                ct);

                            // Generate summary
                            var categorizer = new DifferenceCategorizer();
                            var summary = categorizer.CategorizeAndSummarize(comparisonResult);

                            // Create result
                            var pairResult = new FilePairComparisonResult
                            {
                                File1Name = Path.GetFileName(relativePath),
                                File2Name = Path.GetFileName(relativePath),
                                Result = comparisonResult,
                                Summary = summary
                            };

                            // Update result
                            filePairResults.Add(pairResult);

                            // Update equality flag in a thread-safe way
                            if (!summary.AreEqual)
                            {
                                Interlocked.Exchange(ref equalityFlag, 0);
                            }

                            // Update progress
                            int completed = Interlocked.Increment(ref completedPairs);
                            progress?.Report(new ComparisonProgress(
                                completed,
                                filePairs.Count,
                                $"Compared {completed} of {filePairs.Count} files"));
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error comparing file pair {Path}", filePair.RelativePath);
                        }
                    });

                // Force garbage collection between batches
                GC.Collect(2, GCCollectionMode.Forced, true);
            }

            // Convert from ConcurrentBag to List and sort
            result.FilePairResults = filePairResults
                .OrderBy(r => r.File1Name)
                .ToList();

            result.AllEqual = equalityFlag == 1; // Convert from int flag to bool

            logger.LogInformation("Directory comparison completed. {Equal} equal, {Different} different",
                result.FilePairResults.Count(r => r.AreEqual),
                result.FilePairResults.Count(r => !r.AreEqual));

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

                patternAnalysis = await comparisonService.AnalyzePatternsAsync(result, cancellationToken);

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
                        cancellationToken);

                    // Store semantic analysis in result metadata
                    result.Metadata["SemanticAnalysis"] = semanticAnalysis;
                }

                // Store pattern analysis in result metadata
                result.Metadata["PatternAnalysis"] = patternAnalysis;
            }

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing directories {Dir1} and {Dir2}",
                directory1Path, directory2Path);
            throw;
        }
    }

    /// <summary>
    /// Compare uploaded folders from browser
    /// </summary>
    public async Task<MultiFolderComparisonResult> CompareFolderUploadsAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        bool enablePatternAnalysis = true,
        bool enableSemanticAnalysis = true,
        IProgress<ComparisonProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        return await _performanceTracker.TrackOperationAsync("CompareFolderUploadsAsync", async () => 
        {
            // Record timing statistics about input
            _performanceTracker.TrackOperation("Folder_Stats", () => {
                logger.LogInformation(
                    "Starting comparison of {Folder1Count} files in folder 1 and {Folder2Count} files in folder 2",
                    folder1Files.Count, folder2Files.Count);
            });
            
            // Sort files by name for consistent ordering
            var sortedFiles = _performanceTracker.TrackOperation("Sort_Files", () => {
                var f1 = folder1Files.OrderBy(f => Path.GetFileName(f)).ToList();
                var f2 = folder2Files.OrderBy(f => Path.GetFileName(f)).ToList();
                return (f1, f2);
            });
            
            folder1Files = sortedFiles.f1;
            folder2Files = sortedFiles.f2;
            
            // Determine how many pairs we can make (minimum of both lists)
            int pairCount = Math.Min(folder1Files.Count, folder2Files.Count);
            
            progress?.Report(new ComparisonProgress(0, pairCount, $"Will compare {pairCount} files in order"));
            
            var result = new MultiFolderComparisonResult
            {
                TotalPairsCompared = pairCount,
                AllEqual = true,
                FilePairResults = new List<FilePairComparisonResult>(),
                Metadata = new Dictionary<string, object>()
            };
            
            int equalityFlag = 1;
            int processed = 0;
            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
            
            // Process files by index position instead of matching names
            await _performanceTracker.TrackOperationAsync("Parallel_Comparison", async () => {
                await Parallel.ForEachAsync(
                    Enumerable.Range(0, pairCount),
                    new ParallelOptions { 
                        MaxDegreeOfParallelism = CalculateParallelism(pairCount, folder1Files), 
                        CancellationToken = cancellationToken 
                    },
                    async (index, ct) =>
                    {
                        try
                        {
                            var file1Path = folder1Files[index];
                            var file2Path = folder2Files[index];
                            
                            // Get display names for the UI
                            var file1Name = Path.GetFileName(file1Path);
                            var file2Name = Path.GetFileName(file2Path);
                            
                            var operationId = _performanceTracker.StartOperation($"Compare_File_{index}");
                            
                            try
                            {
                                using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct);
                                using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct);
                                
                                var comparisonResult = await comparisonService.CompareXmlFilesAsync(file1Stream, file2Stream, modelName, ct);
                                var categorizer = new DifferenceCategorizer();
                                var summary = categorizer.CategorizeAndSummarize(comparisonResult);
                                
                                var pairResult = new FilePairComparisonResult
                                {
                                    File1Name = file1Name,
                                    File2Name = file2Name,
                                    Result = comparisonResult,
                                    Summary = summary
                                };
                                
                                filePairResults.Add(pairResult);
                                
                                if (!summary.AreEqual)
                                {
                                    Interlocked.Exchange(ref equalityFlag, 0);
                                }
                            }
                            finally
                            {
                                _performanceTracker.StopOperation(operationId);
                            }
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex, "Error comparing file pair at index {Index}", index);
                        }
                        
                        int current = Interlocked.Increment(ref processed);
                        if (current % 10 == 0 || current == pairCount)
                        {
                            progress?.Report(new ComparisonProgress(current, pairCount, $"Compared {current} of {pairCount} files"));
                        }
                    });
            });
            
            // Process results
            await _performanceTracker.TrackOperationAsync("Process_Results", async () => {
                result.FilePairResults = filePairResults.OrderBy(r => r.File1Name).ToList();
                result.AllEqual = equalityFlag == 1;
                
                logger.LogInformation("Comparison completed. {Equal} equal, {Different} different", 
                    result.FilePairResults.Count(r => r.Summary.AreEqual), 
                    result.FilePairResults.Count(r => !r.Summary.AreEqual));
                    
                progress?.Report(new ComparisonProgress(pairCount, pairCount, "Comparison completed"));
            });
            
            // Pattern and semantic analysis
            if (enablePatternAnalysis && !result.AllEqual && result.FilePairResults.Count > 1)
            {
                await _performanceTracker.TrackOperationAsync("Pattern_Analysis", async () => {
                    progress?.Report(new ComparisonProgress(pairCount, pairCount, "Analyzing patterns..."));
                    var patternAnalysis = await comparisonService.AnalyzePatternsAsync(result, cancellationToken);
                    result.Metadata["PatternAnalysis"] = patternAnalysis;
                    
                    if (enableSemanticAnalysis && patternAnalysis != null)
                    {
                        await _performanceTracker.TrackOperationAsync("Semantic_Analysis", async () => {
                            progress?.Report(new ComparisonProgress(pairCount, pairCount, "Analyzing semantic differences..."));
                            var semanticAnalysis = await comparisonService.AnalyzeSemanticDifferencesAsync(
                                result, patternAnalysis, cancellationToken);
                            result.Metadata["SemanticAnalysis"] = semanticAnalysis;
                        });
                    }
                });
            }
            
            // Log performance data
            _performanceTracker.LogReport();
            
            // Save performance reports to files
            var reportsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "PerformanceReports");
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var baseFileName = $"FolderComparison_{timestamp}";
            _performanceTracker.SaveReportToFile(Path.Combine(reportsDirectory, $"{baseFileName}.txt"));
            _performanceTracker.SaveReportToCsv(Path.Combine(reportsDirectory, $"{baseFileName}.csv"));
            
            return result;
        });
    }

    /// <summary>
    /// Calculate batch size based on file count
    /// </summary>
    private int CalculateBatchSize(int fileCount)
    {
        // For very large file sets, use smaller batches
        if (fileCount > 2000)
            return 20;

        // For large file sets, use moderate batches
        if (fileCount > 500)
            return 50;

        // For smaller file sets, use larger batches
        if (fileCount > 100)
            return 100;

        // For very small sets, process all at once
        return fileCount;
    }

    /// <summary>
    /// Calculate optimal parallelism based on file count, file sizes, and system resources
    /// </summary>
    private int CalculateParallelism(int fileCount, IEnumerable<string> sampleFiles = null)
    {
        // Use the resource monitor to determine optimal parallelism
        long averageFileSizeKb = 0;
        
        // If sample files were provided, estimate average file size
        if (sampleFiles != null)
        {
            averageFileSizeKb = _performanceTracker.TrackOperation("Calculate_Avg_FileSize", () => 
                _resourceMonitor.CalculateAverageFileSizeKb(sampleFiles.Take(Math.Min(20, fileCount))));
        }
        
        return _resourceMonitor.CalculateOptimalParallelism(fileCount, averageFileSizeKb);
    }
}