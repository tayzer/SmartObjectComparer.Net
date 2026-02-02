// <copyright file="ComparisonOrchestrator.cs" company="PlaceholderCompany">



using System.Collections.Concurrent;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Orchestrator responsible for handling file-level comparison operations.
/// </summary>
public class ComparisonOrchestrator : IComparisonOrchestrator
{
    /// <summary>
    /// Threshold for switching to high-performance pipeline (number of file pairs).
    /// </summary>
    private const int HighPerformancePipelineThreshold = 100;

    private readonly ILogger<ComparisonOrchestrator> logger;
    private readonly IXmlDeserializationService deserializationService;
    private readonly DeserializationServiceFactory? deserializationFactory;
    private readonly IComparisonConfigurationService configService;
    private readonly IFileSystemService fileSystemService;
    private readonly PerformanceTracker performanceTracker;
    private readonly SystemResourceMonitor resourceMonitor;
    private readonly ComparisonResultCacheService cacheService;
    private readonly IComparisonEngine comparisonEngine;

    // High-performance pipeline for large batch operations
    private readonly Lazy<HighPerformanceComparisonPipeline> highPerformancePipeline;

    public ComparisonOrchestrator(
        ILogger<ComparisonOrchestrator> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        IFileSystemService fileSystemService,
        PerformanceTracker performanceTracker,
        SystemResourceMonitor resourceMonitor,
        ComparisonResultCacheService cacheService,
        IComparisonEngine comparisonEngine,
        DeserializationServiceFactory? deserializationFactory = null,
        ILoggerFactory? loggerFactory = null)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.fileSystemService = fileSystemService;
        this.performanceTracker = performanceTracker;
        this.resourceMonitor = resourceMonitor;
        this.cacheService = cacheService;
        this.comparisonEngine = comparisonEngine;
        this.deserializationFactory = deserializationFactory;

        // Lazy-initialize high-performance pipeline to avoid circular dependencies
        highPerformancePipeline = new Lazy<HighPerformanceComparisonPipeline>(() =>
        {
            var pipelineLogger = loggerFactory?.CreateLogger<HighPerformanceComparisonPipeline>()
                ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HighPerformanceComparisonPipeline>.Instance;
            return new HighPerformanceComparisonPipeline(
                pipelineLogger,
                configService,
                deserializationService,
                performanceTracker,
                deserializationFactory);
        });
    }

    /// <summary>
    /// Compare two XML files using the specified domain model with caching and performance optimization.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<ComparisonResult> CompareXmlFilesWithCachingAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default) =>
        await performanceTracker.TrackOperationAsync("CompareXmlFilesWithCaching", async () =>
        {
            try
            {
                // Generate configuration fingerprint for caching
                var configFingerprint = cacheService.GenerateConfigurationFingerprint(configService);

                // Generate file hashes for cache keys
                var file1Hash = cacheService.GenerateFileHash(oldXmlStream);
                var file2Hash = cacheService.GenerateFileHash(newXmlStream);

                // Try to get cached comparison result first
                if (cacheService.TryGetCachedComparison(file1Hash, file2Hash, configFingerprint, out var cachedResult))
                {
                    logger.LogDebug(
                        "Using cached comparison result for files with hashes {File1Hash}..{File2Hash}",
                        file1Hash[..8],
                        file2Hash[..8]);
                    return cachedResult;
                }

                logger.LogDebug("Cache miss - performing fresh comparison for {ModelName}", modelName);

                // Check if model exists (will throw if not found)
                var modelType = deserializationService.GetModelType(modelName);

                var deserializeMethod = typeof(IXmlDeserializationService)
                    .GetMethod(nameof(IXmlDeserializationService.DeserializeXml))
                    .MakeGenericMethod(modelType);

                // CRITICAL FIX: Eliminate cloning entirely - use original deserialized objects
                var oldResponse = await performanceTracker.TrackOperationAsync($"Deserialize_Old_{modelName}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            oldXmlStream.Position = 0;
                            return deserializeMethod.Invoke(deserializationService, new object[] { oldXmlStream });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                var newResponse = await performanceTracker.TrackOperationAsync($"Deserialize_New_{modelName}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            newXmlStream.Position = 0;
                            return deserializeMethod.Invoke(deserializationService, new object[] { newXmlStream });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Validate deserialization results
                if (oldResponse == null || newResponse == null)
                {
                    logger.LogError("Deserialization returned null for model {ModelName}", modelName);
                    throw new InvalidOperationException($"Deserialization returned null for model {modelName}");
                }

                // Use the comparison engine for the actual comparison
                var result = await comparisonEngine.CompareObjectsAsync(oldResponse, newResponse, modelType, cancellationToken).ConfigureAwait(false);

                // Cache the result for future use
                cacheService.CacheComparison(file1Hash, file2Hash, configFingerprint, result);

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while comparing XML files with caching");
                throw;
            }
        }).ConfigureAwait(false);

    /// <summary>
    /// Compare two XML files using the specified domain model.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default) =>
        await performanceTracker.TrackOperationAsync("CompareXmlFilesAsync", async () =>
        {
            try
            {
                logger.LogDebug("Starting comparison of XML files using model {ModelName}", modelName);

                // Check if model exists (will throw if not found)
                var modelType = deserializationService.GetModelType(modelName);

                var deserializeMethod = typeof(IXmlDeserializationService)
                    .GetMethod(nameof(IXmlDeserializationService.DeserializeXml))
                    .MakeGenericMethod(modelType);

                // CRITICAL FIX: Eliminate cloning entirely - use original deserialized objects
                var oldResponse = await performanceTracker.TrackOperationAsync($"Deserialize_Old_{modelName}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            oldXmlStream.Position = 0;
                            return deserializeMethod.Invoke(deserializationService, new object[] { oldXmlStream });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                var newResponse = await performanceTracker.TrackOperationAsync($"Deserialize_New_{modelName}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            newXmlStream.Position = 0;
                            return deserializeMethod.Invoke(deserializationService, new object[] { newXmlStream });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Validate deserialization results
                if (oldResponse == null || newResponse == null)
                {
                    logger.LogError("Deserialization returned null for model {ModelName}", modelName);
                    throw new InvalidOperationException($"Deserialization returned null for model {modelName}");
                }

                // Use the comparison engine for the actual comparison
                var result = await comparisonEngine.CompareObjectsAsync(oldResponse, newResponse, modelType, cancellationToken).ConfigureAwait(false);

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while comparing XML files");
                throw;
            }
        }).ConfigureAwait(false);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON) with caching.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<ComparisonResult> CompareFilesWithCachingAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default)
    {
        if (deserializationFactory == null)
        {
            // Fallback to XML-only comparison for backward compatibility
            logger.LogDebug("DeserializationFactory not available, falling back to XML-only comparison");
            return await CompareXmlFilesWithCachingAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken).ConfigureAwait(false);
        }

        return await performanceTracker.TrackOperationAsync("CompareFilesWithCaching", async () =>
        {
            try
            {
                // Detect file formats
                var oldFormat = FileTypeDetector.DetectFormat(oldFilePath);
                var newFormat = FileTypeDetector.DetectFormat(newFilePath);

                if (oldFormat != newFormat)
                {
                    throw new InvalidOperationException($"Cannot compare files of different formats: {oldFormat} vs {newFormat}");
                }

                logger.LogDebug("Comparing files in {Format} format: {OldFile} vs {NewFile}", oldFormat, oldFilePath, newFilePath);

                // Get appropriate deserialization service
                var deserializationService = deserializationFactory.GetService(oldFormat);

                // Generate configuration fingerprint for caching
                var configFingerprint = cacheService.GenerateConfigurationFingerprint(configService);

                // Generate file hashes for cache keys
                var file1Hash = cacheService.GenerateFileHash(oldFileStream);
                var file2Hash = cacheService.GenerateFileHash(newFileStream);

                // Try to get cached comparison result first
                if (cacheService.TryGetCachedComparison(file1Hash, file2Hash, configFingerprint, out var cachedResult))
                {
                    logger.LogDebug(
                        "Using cached comparison result for files with hashes {File1Hash}..{File2Hash}",
                        file1Hash[..8],
                        file2Hash[..8]);
                    return cachedResult;
                }

                logger.LogDebug(
                    "Cache miss - performing fresh comparison for {ModelName} in {Format} format",
                    modelName,
                    oldFormat);

                // Check if model exists (will throw if not found)
                var modelType = deserializationService.GetModelType(modelName);

                // Deserialize both files using the proper domain type
                var oldResponse = await performanceTracker.TrackOperationAsync($"Deserialize_Old_{modelName}_{oldFormat}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            oldFileStream.Position = 0;

                            // Use the proper domain type instead of object to avoid JsonElement issues
                            var deserializeMethod = deserializationService.GetType().GetMethod("Deserialize").MakeGenericMethod(modelType);
                            return deserializeMethod.Invoke(deserializationService, new object[] { oldFileStream, oldFormat });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                var newResponse = await performanceTracker.TrackOperationAsync($"Deserialize_New_{modelName}_{newFormat}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            newFileStream.Position = 0;

                            // Use the proper domain type instead of object to avoid JsonElement issues
                            var deserializeMethod = deserializationService.GetType().GetMethod("Deserialize").MakeGenericMethod(modelType);
                            return deserializeMethod.Invoke(deserializationService, new object[] { newFileStream, newFormat });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Validate deserialization results
                if (oldResponse == null || newResponse == null)
                {
                    logger.LogError("Deserialization returned null for model {ModelName}", modelName);
                    throw new InvalidOperationException($"Deserialization returned null for model {modelName}");
                }

                // Use the comparison engine for the actual comparison
                var result = await comparisonEngine.CompareObjectsAsync(oldResponse, newResponse, modelType, cancellationToken).ConfigureAwait(false);

                // Cache the result for future use
                cacheService.CacheComparison(file1Hash, file2Hash, configFingerprint, result);

                return result;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while comparing files with caching");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON).
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<ComparisonResult> CompareFilesAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default)
    {
        if (deserializationFactory == null)
        {
            // Fallback to XML-only comparison for backward compatibility
            logger.LogDebug("DeserializationFactory not available, falling back to XML-only comparison");
            return await CompareXmlFilesAsync(oldFileStream, newFileStream, modelName, cancellationToken).ConfigureAwait(false);
        }

        return await performanceTracker.TrackOperationAsync("CompareFilesAsync", async () =>
        {
            try
            {
                // Detect file formats
                var oldFormat = FileTypeDetector.DetectFormat(oldFilePath);
                var newFormat = FileTypeDetector.DetectFormat(newFilePath);

                if (oldFormat != newFormat)
                {
                    throw new InvalidOperationException($"Cannot compare files of different formats: {oldFormat} vs {newFormat}");
                }

                logger.LogDebug("Starting comparison of files in {Format} format using model {ModelName}", oldFormat, modelName);

                // Get appropriate deserialization service
                var deserializationService = deserializationFactory.GetService(oldFormat);

                // Check if model exists (will throw if not found)
                var modelType = deserializationService.GetModelType(modelName);

                // Deserialize both files using the proper domain type
                var oldResponse = await performanceTracker.TrackOperationAsync($"Deserialize_Old_{modelName}_{oldFormat}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            oldFileStream.Position = 0;

                            // Use the proper domain type instead of object to avoid JsonElement issues
                            var deserializeMethod = deserializationService.GetType().GetMethod("Deserialize").MakeGenericMethod(modelType);
                            return deserializeMethod.Invoke(deserializationService, new object[] { oldFileStream, oldFormat });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                var newResponse = await performanceTracker.TrackOperationAsync($"Deserialize_New_{modelName}_{newFormat}", async () =>
                {
                    return await Task.Run(
                        () =>
                        {
                            newFileStream.Position = 0;

                            // Use the proper domain type instead of object to avoid JsonElement issues
                            var deserializeMethod = deserializationService.GetType().GetMethod("Deserialize").MakeGenericMethod(modelType);
                            return deserializeMethod.Invoke(deserializationService, new object[] { newFileStream, newFormat });
                        }, cancellationToken).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // Validate deserialization results
                if (oldResponse == null || newResponse == null)
                {
                    logger.LogError("Deserialization returned null for model {ModelName}", modelName);
                    throw new InvalidOperationException($"Deserialization returned null for model {modelName}");
                }

                // Use the comparison engine for the actual comparison
                return await comparisonEngine.CompareObjectsAsync(oldResponse, newResponse, modelType, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while comparing files");
                throw;
            }
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Compare multiple folder pairs of files.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiFolderComparisonResult();
        var pairCount = Math.Min(folder1Files.Count, folder2Files.Count);
        result.TotalPairsCompared = pairCount;

        if (pairCount == 0)
        {
            logger.LogWarning("No file pairs to compare");
            return result;
        }

        logger.LogInformation("Starting comparison of {PairCount} file pairs using model {ModelName}", pairCount, modelName);

        for (var i = 0; i < pairCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file1Path = folder1Files[i];
            var file2Path = folder2Files[i];
            try
            {
                // Only log individual file pairs in debug mode to avoid spam with large comparisons
                if (logger.IsEnabled(LogLevel.Debug))
                {
                    logger.LogDebug("Comparing pair {PairNumber}/{TotalPairs}: {File1} vs {File2}", i + 1, pairCount, file1Path, file2Path);
                }

                using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, cancellationToken).ConfigureAwait(false);
                using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, cancellationToken).ConfigureAwait(false);
                var pairResult = await CompareFilesWithCachingAsync(file1Stream, file2Stream, modelName, file1Path, file2Path, cancellationToken).ConfigureAwait(false);
                var categorizer = new DifferenceCategorizer();
                var summary = categorizer.CategorizeAndSummarize(pairResult);
                var filePairResult = new FilePairComparisonResult
                {
                    File1Name = Path.GetFileName(file1Path),
                    File2Name = Path.GetFileName(file2Path),
                    Result = pairResult,
                    Summary = summary,
                };
                result.FilePairResults.Add(filePairResult);
                if (!summary.AreEqual)
                {
                    result.AllEqual = false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing files {File1} and {File2}", file1Path, file2Path);
                throw;
            }
        }

        var equalCount = result.FilePairResults.Count(r => r.Summary?.AreEqual ?? true);
        var differentCount = result.FilePairResults.Count(r => !(r.Summary?.AreEqual ?? true));
        logger.LogInformation("Folder comparison completed. {EqualCount} equal, {DifferentCount} different", equalCount, differentCount);
        return result;
    }

    /// <summary>
    /// Compare multiple folder pairs of files in batches with parallel processing.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 25,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default) =>
        await performanceTracker.TrackOperationAsync("CompareFoldersInBatchesAsync", async () =>
        {
            logger.LogInformation(
                "Starting batch comparison of {Count1} files from folder 1 and {Count2} files from folder 2",
                folder1Files.Count,
                folder2Files.Count);

            // Create mappings between files in both folders (by name for now)
            var filePairMappings = FilePairMappingUtility.CreateFilePairMappings(folder1Files, folder2Files);
            var totalPairs = filePairMappings.Count;

            // Use high-performance pipeline for large batch operations
            if (totalPairs >= HighPerformancePipelineThreshold)
            {
                logger.LogInformation(
                    "Using high-performance pipeline for {TotalPairs} file pairs (threshold: {Threshold})",
                    totalPairs,
                    HighPerformancePipelineThreshold);

                // Create a progress adapter to convert ComparisonProgress to the tuple format
                IProgress<ComparisonProgress>? pipelineProgress = null;
                if (progress != null)
                {
                    pipelineProgress = new Progress<ComparisonProgress>(p => progress.Report((p.Completed, p.Total)));
                }

                return await highPerformancePipeline.Value.CompareFilesAsync(
                    filePairMappings,
                    modelName,
                    pipelineProgress,
                    cancellationToken).ConfigureAwait(false);
            }

            // For smaller batches, use the standard approach
            logger.LogDebug(
                "Using standard batch processing for {TotalPairs} file pairs (below threshold: {Threshold})",
                totalPairs,
                HighPerformancePipelineThreshold);

            // Estimate optimal batch size based on file count and system resources
            var optimalBatchSize = CalculateOptimalBatchSize(Math.Max(folder1Files.Count, folder2Files.Count), folder1Files);

            // Use the provided batch size if specified, otherwise use the calculated one
            if (batchSize <= 0)
            {
                batchSize = optimalBatchSize;
                logger.LogInformation("Using calculated optimal batch size: {BatchSize}", batchSize);
            }
            else
            {
                logger.LogInformation("Using provided batch size: {BatchSize}", batchSize);
            }

            // Report batch information
            var batchCount = (int)Math.Ceiling((double)totalPairs / batchSize);

            progress?.Report((0, totalPairs));

            var result = new MultiFolderComparisonResult
            {
                TotalPairsCompared = totalPairs,
                AllEqual = true,
                FilePairResults = new List<FilePairComparisonResult>(),
                Metadata = new Dictionary<string, object>(StringComparer.Ordinal),
            };

            // Create empty results list
            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
            var processedCount = 0;
            var equalityFlag = 1;  // Assume all equal until proven otherwise

            // Process in batches
            for (var batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                // Get the current batch of files
                var batchStart = batchIndex * batchSize;
                var batchEnd = Math.Min(batchStart + batchSize, totalPairs);
                var batchFilePairs = filePairMappings.Skip(batchStart).Take(batchEnd - batchStart).ToList();

                // Recalculate parallelism for this specific batch
                var batchParallelism = CalculateOptimalParallelism(
                    batchFilePairs.Count,
                    batchFilePairs.Select(p => p.file1Path));

                logger.LogDebug(
                    "Processing batch {BatchIndex}/{BatchCount} with {FileCount} files using parallelism of {Parallelism}",
                    batchIndex + 1,
                    batchCount,
                    batchFilePairs.Count,
                    batchParallelism);

                // Process this batch in parallel
                await performanceTracker.TrackOperationAsync($"Batch_{batchIndex + 1}", async () =>
                {
                    await Parallel.ForEachAsync(
                        batchFilePairs,
                        new ParallelOptions
                        {
                            MaxDegreeOfParallelism = batchParallelism,
                            CancellationToken = cancellationToken,
                        },
                        async (filePair, ct) =>
                        {
                            var (file1Path, file2Path, relativePath) = filePair;
                            var file1Name = Path.GetFileName(file1Path);
                            var file2Name = Path.GetFileName(file2Path);

                            try
                            {
                                var operationId = performanceTracker.StartOperation($"Compare_File_{file1Name}");

                                try
                                {
                                    // Open file streams without loading entirely into memory
                                    using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, ct).ConfigureAwait(false);
                                    using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, ct).ConfigureAwait(false);

                                    // Perform comparison with caching using format-agnostic method
                                    var comparisonResult = await CompareFilesWithCachingAsync(
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
                                        File1Name = file1Name,
                                        File2Name = file2Name,
                                        Result = comparisonResult,
                                        Summary = summary,
                                    };

                                    // Update result
                                    filePairResults.Add(pairResult);

                                    // If any differences, flag the overall result
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
                                    "Error comparing files {File1} and {File2}: {Message}",
                                    file1Path,
                                    file2Path,
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

                                // Mark as not equal since we couldn't determine the result
                                Interlocked.Exchange(ref equalityFlag, 0);
                            }

                            // Update progress
                            var currentProcessed = Interlocked.Increment(ref processedCount);
                            if (currentProcessed % 10 == 0 || currentProcessed == totalPairs)
                            {
                                progress?.Report((currentProcessed, totalPairs));
                            }
                        }).ConfigureAwait(false);
                }).ConfigureAwait(false);

                // If adaptive throttling is needed based on system resources, add it here
                if (batchIndex < batchCount - 1)
                {
                    var cpuUsage = resourceMonitor.GetCpuUsage();
                    var memoryUsage = resourceMonitor.GetMemoryUsage();

                    // If system is under heavy load, add a delay between batches
                    if (cpuUsage > 90 || memoryUsage > 90)
                    {
                        logger.LogInformation(
                            "System under load (CPU: {CpuUsage}%, Memory: {MemoryUsage}%), adding delay between batches",
                            cpuUsage,
                            memoryUsage);
                        await Task.Delay(1000, cancellationToken).ConfigureAwait(false); // 1 second delay
                    }
                    else if (cpuUsage > 75 || memoryUsage > 75)
                    {
                        logger.LogInformation(
                            "System under moderate load (CPU: {CpuUsage}%, Memory: {MemoryUsage}%), adding short delay",
                            cpuUsage,
                            memoryUsage);
                        await Task.Delay(300, cancellationToken).ConfigureAwait(false); // 300ms delay
                    }
                }
            }

            // Complete the result
            result.FilePairResults = filePairResults.ToList();
            result.AllEqual = equalityFlag == 1;

            logger.LogInformation(
                "Batch comparison completed. Processed {Processed}/{Total} file pairs. Equal: {AllEqual}",
                processedCount,
                totalPairs,
                result.AllEqual);

            progress?.Report((totalPairs, totalPairs));

            return result;
        }).ConfigureAwait(false);

    /// <summary>
    /// Calculate optimal batch size based on file count and system resources.
    /// </summary>
    private int CalculateOptimalBatchSize(int fileCount, IEnumerable<string>? sampleFiles = null)
    {
        // Base calculation on file count
        var baseBatchSize = Math.Max(10, Math.Min(50, fileCount / 4));

        // Adjust based on available system resources
        var cpuCores = Environment.ProcessorCount;
        var memoryGB = GC.GetTotalMemory(false) / (1024 * 1024 * 1024);

        // Conservative approach: use fewer cores for memory-intensive operations
        var optimalParallelism = Math.Max(1, Math.Min(cpuCores - 1, 4));

        // Adjust batch size based on parallelism
        var adjustedBatchSize = baseBatchSize * optimalParallelism;

        // Cap at reasonable limits
        adjustedBatchSize = Math.Min(adjustedBatchSize, 100);
        adjustedBatchSize = Math.Max(adjustedBatchSize, 10);

        logger.LogDebug(
            "Calculated optimal batch size: {BatchSize} (files: {FileCount}, cores: {Cores}, memory: {Memory}GB)",
            adjustedBatchSize,
            fileCount,
            cpuCores,
            memoryGB);

        return adjustedBatchSize;
    }

    /// <summary>
    /// Calculate optimal parallelism for batch processing.
    /// </summary>
    private int CalculateOptimalParallelism(int fileCount, IEnumerable<string>? sampleFiles = null)
    {
        // Base calculation on available CPU cores
        var cpuCores = Environment.ProcessorCount;

        // Conservative approach: use fewer cores for memory-intensive operations
        var optimalParallelism = Math.Max(1, Math.Min(cpuCores - 1, 4));

        // Adjust based on file count (don't over-parallelize for small batches)
        if (fileCount < optimalParallelism * 2)
        {
            optimalParallelism = Math.Max(1, fileCount / 2);
        }

        logger.LogDebug(
            "Calculated optimal parallelism: {Parallelism} (files: {FileCount}, cores: {Cores})",
            optimalParallelism,
            fileCount,
            cpuCores);

        return optimalParallelism;
    }
}
