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

    public DirectoryComparisonService(
        ILogger<DirectoryComparisonService> logger,
        IComparisonService comparisonService,
        IFileSystemService fileSystemService,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService)
    {
        this.logger = logger;
        this.comparisonService = comparisonService;
        this.fileSystemService = fileSystemService;
        this.deserializationService = deserializationService;
        this.configService = configService;
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
                                File1Name = relativePath,
                                File2Name = relativePath,
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
        // Find matching file pairs by name
        var fileNames1 = new HashSet<string>(folder1Files.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
        var fileNames2 = new HashSet<string>(folder2Files.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);
        var commonFiles = fileNames1.Intersect(fileNames2, StringComparer.OrdinalIgnoreCase).ToList();
        progress?.Report(new ComparisonProgress(0, commonFiles.Count, $"Found {commonFiles.Count} matching files"));
        var result = new MultiFolderComparisonResult
        {
            TotalPairsCompared = commonFiles.Count,
            AllEqual = true,
            FilePairResults = new List<FilePairComparisonResult>(),
            Metadata = new Dictionary<string, object>()
        };
        int equalityFlag = 1;
        int processed = 0;
        var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
        await Parallel.ForEachAsync(
            commonFiles,
            new ParallelOptions { MaxDegreeOfParallelism = CalculateParallelism(commonFiles.Count), CancellationToken = cancellationToken },
            async (fileName, ct) =>
            {
                try
                {
                    var file1Path = folder1Files.First(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    var file2Path = folder2Files.First(f => Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));
                    using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct);
                    using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct);
                    var comparisonResult = await comparisonService.CompareXmlFilesAsync(file1Stream, file2Stream, modelName, ct);
                    var categorizer = new DifferenceCategorizer();
                    var summary = categorizer.CategorizeAndSummarize(comparisonResult);
                    var pairResult = new FilePairComparisonResult
                    {
                        File1Name = fileName,
                        File2Name = fileName,
                        Result = comparisonResult,
                        Summary = summary
                    };
                    filePairResults.Add(pairResult);
                    if (!summary.AreEqual)
                    {
                        Interlocked.Exchange(ref equalityFlag, 0);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error comparing file {FileName}", fileName);
                }
                int current = Interlocked.Increment(ref processed);
                if (current % 10 == 0 || current == commonFiles.Count)
                {
                    progress?.Report(new ComparisonProgress(current, commonFiles.Count, $"Compared {current} of {commonFiles.Count} files"));
                }
            });
        result.FilePairResults = filePairResults.OrderBy(r => r.File1Name).ToList();
        result.AllEqual = equalityFlag == 1;
        logger.LogInformation("Upload comparison completed. {Equal} equal, {Different} different", result.FilePairResults.Count(r => r.AreEqual), result.FilePairResults.Count(r => !r.AreEqual));
        progress?.Report(new ComparisonProgress(commonFiles.Count, commonFiles.Count, "Comparison completed"));
        // Pattern and semantic analysis (unchanged)
        ComparisonPatternAnalysis patternAnalysis = null;
        if (enablePatternAnalysis && !result.AllEqual && result.FilePairResults.Count > 1)
        {
            progress?.Report(new ComparisonProgress(commonFiles.Count, commonFiles.Count, "Analyzing patterns..."));
            patternAnalysis = await comparisonService.AnalyzePatternsAsync(result, cancellationToken);
            if (enableSemanticAnalysis && patternAnalysis != null)
            {
                progress?.Report(new ComparisonProgress(commonFiles.Count, commonFiles.Count, "Analyzing semantic differences..."));
                var semanticAnalysis = await comparisonService.AnalyzeSemanticDifferencesAsync(result, patternAnalysis, cancellationToken);
                result.Metadata["SemanticAnalysis"] = semanticAnalysis;
            }
            result.Metadata["PatternAnalysis"] = patternAnalysis;
        }
        return result;
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
    /// Calculate parallelism based on batch size
    /// </summary>
    private int CalculateParallelism(int batchSize)
    {
        var processorCount = Environment.ProcessorCount;

        // For very small batches, limit parallelism
        if (batchSize < 10)
            return Math.Max(1, processorCount / 4);

        // For small batches, use moderate parallelism
        if (batchSize < 50)
            return Math.Max(2, processorCount / 2);

        // For moderate batches, use high parallelism
        if (batchSize < 200)
            return Math.Max(4, processorCount - 1);

        // For large batches, use full parallelism
        return processorCount;
    }
}