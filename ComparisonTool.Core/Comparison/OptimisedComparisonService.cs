using System.Text.RegularExpressions;
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
/// Service responsible for executing comparisons between objects and handling comparison results
/// Optimized for large file sets with memory management and efficient batch processing
/// </summary>
public class OptimizedComparisonService : IComparisonService
{
    private readonly ILogger<OptimizedComparisonService> logger;
    private readonly IXmlDeserializationService deserializationService;
    private readonly IComparisonConfigurationService configService;
    private readonly IFileSystemService fileSystemService;

    // Memory management settings
    private const int MaxConcurrentComparisons = 4; // Limit concurrent operations
    private const int StandardBatchSize = 50; // Default batch size
    private const int LargeBatchThreshold = 500; // Threshold for large batches
    private const int MemoryReleaseInterval = 100; // Release memory after this many files

    public OptimizedComparisonService(
        ILogger<OptimizedComparisonService> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        IFileSystemService fileSystemService)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.fileSystemService = fileSystemService;
    }

    /// <summary>
    /// Compare two XML files using the specified domain model
    /// </summary>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        try
        {
            logger.LogDebug("Starting comparison of XML files using model {ModelName}", modelName);

            // Check if model exists (will throw if not found)
            var modelType = deserializationService.GetModelType(modelName);

            var deserializeMethod = typeof(IXmlDeserializationService)
                .GetMethod(nameof(IXmlDeserializationService.DeserializeXml))
                .MakeGenericMethod(modelType);

            // Call the methods via reflection
            oldXmlStream.Position = 0;
            newXmlStream.Position = 0;

            var oldResponse = deserializeMethod.Invoke(deserializationService, new object[] { oldXmlStream });
            var newResponse = deserializeMethod.Invoke(deserializationService, new object[] { newXmlStream });

            // Apply configured settings
            configService.ApplyConfiguredSettings();

            // Compare the objects
            var result = await Task.Run(() =>
            {
                var compareLogic = configService.GetCompareLogic();
                return compareLogic.Compare(oldResponse, newResponse);
            }, cancellationToken);

            logger.LogDebug("Comparison completed. Found {DifferenceCount} differences",
                result.Differences.Count);

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred while comparing XML files");
            throw;
        }
    }

    /// <summary>
    /// Compare multiple folder pairs of XML files with optimized memory usage
    /// </summary>
    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        if (folder1Files.Count > LargeBatchThreshold || folder2Files.Count > LargeBatchThreshold)
        {
            return await CompareFoldersInBatchesAsync(
                folder1Files,
                folder2Files,
                modelName,
                StandardBatchSize,
                null,
                cancellationToken);
        }
        var result = new MultiFolderComparisonResult();
        int pairCount = Math.Min(folder1Files.Count, folder2Files.Count);
        result.TotalPairsCompared = pairCount;
        if (pairCount == 0)
        {
            logger.LogWarning("No file pairs to compare");
            return result;
        }
        logger.LogInformation("Starting comparison of {PairCount} file pairs using model {ModelName}", pairCount, modelName);
        for (int i = 0; i < pairCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file1Path = folder1Files[i];
            var file2Path = folder2Files[i];
            try
            {
                logger.LogDebug("Comparing pair {PairNumber}/{TotalPairs}: {File1} vs {File2}", i + 1, pairCount, file1Path, file2Path);
                using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, cancellationToken);
                using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, cancellationToken);
                var pairResult = await CompareXmlFilesAsync(file1Stream, file2Stream, modelName, cancellationToken);
                var categorizer = new DifferenceCategorizer();
                var summary = categorizer.CategorizeAndSummarize(pairResult);
                var filePairResult = new FilePairComparisonResult
                {
                    File1Name = file1Path,
                    File2Name = file2Path,
                    Result = pairResult,
                    Summary = summary
                };
                result.FilePairResults.Add(filePairResult);
                if (!summary.AreEqual)
                {
                    result.AllEqual = false;
                }
                if (i > 0 && i % MemoryReleaseInterval == 0)
                {
                    ReleaseMemory();
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing files {File1} and {File2}", file1Path, file2Path);
                throw;
            }
        }
        logger.LogInformation("Folder comparison completed. {EqualCount} equal, {DifferentCount} different", result.FilePairResults.Count(r => r.AreEqual), result.FilePairResults.Count(r => !r.AreEqual));
        return result;
    }

    /// <summary>
    /// Compare multiple folder pairs of XML files in batches with parallel processing
    /// Optimized for memory usage with very large file sets
    /// </summary>
    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = StandardBatchSize,
        IProgress<(int Completed, int Total)> progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiFolderComparisonResult();
        result.TotalPairsCompared = Math.Min(folder1Files.Count, folder2Files.Count);
        int completedPairs = 0;
        int equalityFlag = 1;
        if (result.TotalPairsCompared == 0)
        {
            logger.LogWarning("No file pairs to compare");
            return result;
        }
        batchSize = AdjustBatchSize(result.TotalPairsCompared, batchSize);
        logger.LogInformation("Starting batch comparison of {PairCount} file pairs using model {ModelName}", result.TotalPairsCompared, modelName);
        var filePairResults = new ConcurrentBag<FilePairComparisonResult>();
        int totalBatches = (result.TotalPairsCompared + batchSize - 1) / batchSize;
        try
        {
            for (int batchIndex = 0; batchIndex < totalBatches; batchIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int batchStartIndex = batchIndex * batchSize;
                int currentBatchSize = Math.Min(batchSize, result.TotalPairsCompared - batchStartIndex);
                logger.LogInformation("Processing batch {BatchNumber}/{TotalBatches} with {BatchSize} files", batchIndex + 1, totalBatches, currentBatchSize);
                var batch1Files = folder1Files.Skip(batchStartIndex).Take(currentBatchSize).ToList();
                var batch2Files = folder2Files.Skip(batchStartIndex).Take(currentBatchSize).ToList();
                using var semaphore = new SemaphoreSlim(MaxConcurrentComparisons);
                var tasks = new List<Task<FilePairComparisonResult>>();
                for (int i = 0; i < currentBatchSize; i++)
                {
                    var localIndex = i;
                    tasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(cancellationToken);
                        try
                        {
                            var file1Path = batch1Files[localIndex];
                            var file2Path = batch2Files[localIndex];
                            using var file1Stream = await fileSystemService.OpenFileStreamAsync(file1Path, cancellationToken);
                            using var file2Stream = await fileSystemService.OpenFileStreamAsync(file2Path, cancellationToken);
                            var comparisonResult = await CompareXmlFilesAsync(file1Stream, file2Stream, modelName, cancellationToken);
                            var categorizer = new DifferenceCategorizer();
                            var summary = categorizer.CategorizeAndSummarize(comparisonResult);
                            return new FilePairComparisonResult
                            {
                                File1Name = file1Path,
                                File2Name = file2Path,
                                Result = comparisonResult,
                                Summary = summary
                            };
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }, cancellationToken));
                }
                while (tasks.Count > 0)
                {
                    var completedTask = await Task.WhenAny(tasks);
                    tasks.Remove(completedTask);
                    try
                    {
                        var pairResult = await completedTask;
                        filePairResults.Add(pairResult);
                        if (!pairResult.AreEqual)
                        {
                            Interlocked.Exchange(ref equalityFlag, 0);
                        }
                        completedPairs++;
                        progress?.Report((completedPairs, result.TotalPairsCompared));
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error in batch file comparison task");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error during batch folder comparison");
            throw;
        }
        result.FilePairResults = filePairResults.OrderBy(r => r.File1Name).ToList();
        result.AllEqual = equalityFlag == 1;
        logger.LogInformation("Batch comparison completed. {EqualCount} equal, {DifferentCount} different", result.FilePairResults.Count(r => r.AreEqual), result.FilePairResults.Count(r => !r.AreEqual));
        return result;
    }

    /// <summary>
    /// Analyze patterns across multiple file comparison results
    /// </summary>
    public async Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
        MultiFolderComparisonResult folderResult,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            logger.LogInformation("Starting pattern analysis of {FileCount} comparison results",
                folderResult.FilePairResults.Count);

            var analysis = new ComparisonPatternAnalysis
            {
                TotalFilesPaired = folderResult.TotalPairsCompared,
                FilesWithDifferences = folderResult.FilePairResults.Count(r => !r.AreEqual),
                TotalDifferences = folderResult.FilePairResults.Sum(r => r.Summary?.TotalDifferenceCount ?? 0)
            };

            // Process in batches for large result sets
            if (folderResult.FilePairResults.Count > LargeBatchThreshold)
            {
                return AnalyzePatternsBatched(folderResult, analysis, cancellationToken);
            }

            // Initialize category counts
            foreach (DifferenceCategory category in Enum.GetValues(typeof(DifferenceCategory)))
            {
                analysis.TotalByCategory[category] = 0;
            }

            // Process all differences to find common patterns
            var allPathPatterns = new Dictionary<string, GlobalPatternInfo>();
            var allPropertyChanges = new Dictionary<string, GlobalPropertyChangeInfo>();

            foreach (var filePair in folderResult.FilePairResults)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (filePair.AreEqual) continue;

                var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";

                // Add to category counts
                foreach (var category in filePair.Summary.DifferencesByChangeType)
                {
                    lock (analysis.TotalByCategory)
                    {
                        if (analysis.TotalByCategory.ContainsKey(category.Key))
                        {
                            analysis.TotalByCategory[category.Key] += category.Value.Count;
                        }
                    }
                }

                // Process each difference
                foreach (var diff in filePair.Result.Differences)
                {
                    // Normalize the property path (remove indices, backing fields)
                    string normalizedPath = NormalizePropertyPath(diff.PropertyName);

                    // Create pattern info if not exists
                    lock (allPathPatterns)
                    {
                        if (!allPathPatterns.ContainsKey(normalizedPath))
                        {
                            allPathPatterns[normalizedPath] = new GlobalPatternInfo
                            {
                                PatternPath = normalizedPath,
                                _occurrenceCount = 0,
                                _fileCount = 0,
                                AffectedFiles = new List<string>(),
                                Examples = new List<Difference>()
                            };
                        }

                        // Update pattern info
                        var patternInfo = allPathPatterns[normalizedPath];
                        patternInfo._occurrenceCount++;

                        if (!patternInfo.AffectedFiles.Contains(pairIdentifier))
                        {
                            patternInfo.AffectedFiles.Add(pairIdentifier);
                            patternInfo._fileCount++;
                        }

                        // Add example if we don't have many
                        if (patternInfo.Examples.Count < 3)
                        {
                            patternInfo.Examples.Add(diff);
                        }
                    }

                    // Track property change info (create key from property + old value + new value)
                    var oldValue = diff.Object1Value?.ToString() ?? "null";
                    var newValue = diff.Object2Value?.ToString() ?? "null";
                    var changeKey = $"{normalizedPath}|{oldValue}|{newValue}";

                    lock (allPropertyChanges)
                    {
                        if (!allPropertyChanges.ContainsKey(changeKey))
                        {
                            allPropertyChanges[changeKey] = new GlobalPropertyChangeInfo
                            {
                                PropertyName = normalizedPath,
                                _occurrenceCount = 0,
                                CommonChanges = new Dictionary<string, string>
                                {
                                    { oldValue, newValue }
                                },
                                AffectedFiles = new List<string>()
                            };
                        }

                        // Update property change info
                        var changeInfo = allPropertyChanges[changeKey];
                        changeInfo._occurrenceCount++;

                        if (!changeInfo.AffectedFiles.Contains(pairIdentifier))
                        {
                            changeInfo.AffectedFiles.Add(pairIdentifier);
                        }
                    }
                }
            }

            // Sort and select most common patterns
            analysis.CommonPathPatterns = allPathPatterns.Values
                .Where(p => p.FileCount > 1) // Only patterns that appear in multiple files
                .OrderByDescending(p => p.FileCount)
                .ThenByDescending(p => p.OccurrenceCount)
                .Take(20) // Limit to top 20 patterns
                .ToList();

            // Sort and select most common property changes
            analysis.CommonPropertyChanges = allPropertyChanges.Values
                .Where(c => c.AffectedFiles.Count > 1) // Only changes that appear in multiple files
                .OrderByDescending(c => c.AffectedFiles.Count)
                .ThenByDescending(c => c.OccurrenceCount)
                .Take(20) // Limit to top 20 common changes
                .ToList();

            // Group similar files based on their difference patterns
            GroupSimilarFiles(folderResult, analysis);

            logger.LogInformation("Pattern analysis completed. Found {PatternCount} common patterns across files",
                analysis.CommonPathPatterns.Count);

            return analysis;
        }, cancellationToken);
    }

    /// <summary>
    /// Analyze patterns in batches for large result sets
    /// </summary>
    private ComparisonPatternAnalysis AnalyzePatternsBatched(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis analysis,
        CancellationToken cancellationToken)
    {
        // Initialize category counts
        foreach (DifferenceCategory category in Enum.GetValues(typeof(DifferenceCategory)))
        {
            analysis.TotalByCategory[category] = 0;
        }

        // Use concurrent collections for thread safety
        var allPathPatterns = new ConcurrentDictionary<string, GlobalPatternInfo>();
        var allPropertyChanges = new ConcurrentDictionary<string, GlobalPropertyChangeInfo>();

        // Dictionary to track category totals
        var categoryTotals = new ConcurrentDictionary<DifferenceCategory, int>();
        foreach (DifferenceCategory category in Enum.GetValues(typeof(DifferenceCategory)))
        {
            categoryTotals[category] = 0;
        }

        // Calculate optimal batch size
        int batchSize = AdjustBatchSize(folderResult.FilePairResults.Count, StandardBatchSize);
        int batchCount = (folderResult.FilePairResults.Count + batchSize - 1) / batchSize;

        logger.LogInformation("Processing pattern analysis in {BatchCount} batches of size {BatchSize}",
            batchCount, batchSize);

        // Process in batches
        for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int startIndex = batchIndex * batchSize;
            int endIndex = Math.Min(startIndex + batchSize, folderResult.FilePairResults.Count);

            // Process this batch of files
            Parallel.For(startIndex, endIndex, new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount / 2, // Limit parallelism
                CancellationToken = cancellationToken
            }, fileIndex =>
            {
                var filePair = folderResult.FilePairResults[fileIndex];
                if (filePair.AreEqual) return;

                var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";

                // Add to category counts - thread safe with ConcurrentDictionary
                foreach (var category in filePair.Summary.DifferencesByChangeType)
                {
                    categoryTotals.AddOrUpdate(
                        category.Key,
                        category.Value.Count,
                        (_, existingCount) => existingCount + category.Value.Count);
                }

                // Process each difference
                foreach (var diff in filePair.Result.Differences)
                {
                    // Normalize the property path (remove indices, backing fields)
                    string normalizedPath = NormalizePropertyPath(diff.PropertyName);

                    // Update or create pattern info - thread safe with ConcurrentDictionary
                    allPathPatterns.AddOrUpdate(
                        normalizedPath,
                        // Create new
                        _ => new GlobalPatternInfo
                        {
                            PatternPath = normalizedPath,
                            _occurrenceCount = 1, // Use the field directly for thread safety
                            _fileCount = 1,       // Use the field directly for thread safety
                            AffectedFiles = new List<string> { pairIdentifier },
                            Examples = new List<Difference> { diff }
                        },
                        // Update existing
                        (_, existing) =>
                        {
                            // Use the field directly for Interlocked operations  
                            Interlocked.Increment(ref existing._occurrenceCount);

                            lock (existing)
                            {
                                if (!existing.AffectedFiles.Contains(pairIdentifier))
                                {
                                    existing.AffectedFiles.Add(pairIdentifier);
                                    Interlocked.Increment(ref existing._fileCount);
                                }

                                if (existing.Examples.Count < 3)
                                {
                                    existing.Examples.Add(diff);
                                }
                            }

                            return existing;
                        });

                    // Similar thread-safe approach for property changes
                    var oldValue = diff.Object1Value?.ToString() ?? "null";
                    var newValue = diff.Object2Value?.ToString() ?? "null";
                    var changeKey = $"{normalizedPath}|{oldValue}|{newValue}";

                    allPropertyChanges.AddOrUpdate(
                        changeKey,
                        // Create new
                        _ => new GlobalPropertyChangeInfo
                        {
                            PropertyName = normalizedPath,
                            _occurrenceCount = 1, // Use the field directly for thread safety
                            CommonChanges = new Dictionary<string, string> { { oldValue, newValue } },
                            AffectedFiles = new List<string> { pairIdentifier }
                        },
                        // Update existing
                        (_, existing) =>
                        {
                            // Use the field directly for Interlocked operations  
                            Interlocked.Increment(ref existing._occurrenceCount);

                            lock (existing)
                            {
                                if (!existing.AffectedFiles.Contains(pairIdentifier))
                                {
                                    existing.AffectedFiles.Add(pairIdentifier);
                                }
                            }

                            return existing;
                        });
                }
            });

            // Release memory between batches
            if (batchIndex < batchCount - 1)
            {
                ReleaseMemory();
            }
        }

        // Update the main analysis object with the totals from our concurrent dictionaries
        foreach (var category in categoryTotals)
        {
            analysis.TotalByCategory[category.Key] = category.Value;
        }

        // Convert and sort the concurrent collections
        analysis.CommonPathPatterns = allPathPatterns.Values
            .Where(p => p.FileCount > 1) // Only patterns that appear in multiple files
            .OrderByDescending(p => p.FileCount)
            .ThenByDescending(p => p.OccurrenceCount)
            .Take(20) // Limit to top 20 patterns
            .ToList();

        analysis.CommonPropertyChanges = allPropertyChanges.Values
            .Where(c => c.AffectedFiles.Count > 1) // Only changes that appear in multiple files
            .OrderByDescending(c => c.AffectedFiles.Count)
            .ThenByDescending(c => c.OccurrenceCount)
            .Take(20) // Limit to top 20 common changes
            .ToList();

        // Group similar files (low priority - do at the end)
        GroupSimilarFiles(folderResult, analysis);

        return analysis;
    }

    /// <summary>
    /// Analyze semantic differences across multiple file comparison results
    /// </summary>
    public async Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis patternAnalysis,
        CancellationToken cancellationToken = default)
    {
        // Existing implementation without changes
        return await Task.Run(() =>
        {
            logger.LogInformation("Starting semantic difference analysis");

            var analyzer = new SemanticDifferenceAnalyzer(folderResult, patternAnalysis);
            var semanticAnalysis = analyzer.AnalyzeSemanticGroups();

            logger.LogInformation("Semantic analysis completed. Found {GroupCount} semantic groups with {DifferenceCount} differences",
                semanticAnalysis.SemanticGroups.Count, semanticAnalysis.CategorizedDifferences);

            return semanticAnalysis;
        }, cancellationToken);
    }

    public async Task<EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult>
        AnalyzeStructualPatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            logger.LogInformation("Starting enhanced structural pattern analysis");

            var analyzer = new EnhancedStructuralDifferenceAnalyzer(folderResult, logger);
            var structuralAnalysis = analyzer.AnalyzeStructuralPatterns();

            logger.LogInformation(
                "Enhanced structural analysis completed. Found {CriticalCound} critical missing elements, {MissingProps} missing properties, and {OrderDiffs} order differences",
                structuralAnalysis.CriticalMissingElements.Count,
                structuralAnalysis.ConsistentlyMissingProperties.Count,
                structuralAnalysis.ElementOrderDifferences.Count);

            return structuralAnalysis;
        }, cancellationToken);
    }

    /// <summary>
    /// Group similar files based on their difference patterns
    /// </summary>
    private void GroupSimilarFiles(MultiFolderComparisonResult folderResult, ComparisonPatternAnalysis analysis)
    {
        // Skip if not enough files with differences
        if (analysis.FilesWithDifferences <= 1)
            return;

        // Create fingerprints of each file's differences
        var fileFingerprints = new Dictionary<string, HashSet<string>>();

        foreach (var filePair in folderResult.FilePairResults)
        {
            if (filePair.AreEqual) continue;

            var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";
            var fingerprint = new HashSet<string>();

            foreach (var diff in filePair.Result.Differences)
            {
                fingerprint.Add(NormalizePropertyPath(diff.PropertyName));
            }

            fileFingerprints[pairIdentifier] = fingerprint;
        }

        // Build similarity matrix
        var similarities = new Dictionary<(string, string), double>();
        var fileIds = fileFingerprints.Keys.ToList();

        for (int i = 0; i < fileIds.Count; i++)
        {
            for (int j = i + 1; j < fileIds.Count; j++)
            {
                var file1 = fileIds[i];
                var file2 = fileIds[j];
                var set1 = fileFingerprints[file1];
                var set2 = fileFingerprints[file2];

                // Calculate Jaccard similarity
                var intersection = set1.Intersect(set2).Count();
                var union = set1.Count + set2.Count - intersection;
                var similarity = (double)intersection / (union == 0 ? 1 : union);

                similarities[(file1, file2)] = similarity;
            }
        }

        // Group files using a simple threshold-based approach
        var grouped = new HashSet<string>();
        var similarityThreshold = 0.6; // 60% similarity to be considered in the same group

        foreach (var similarity in similarities.OrderByDescending(s => s.Value))
        {
            if (similarity.Value < similarityThreshold)
                continue;

            var file1 = similarity.Key.Item1;
            var file2 = similarity.Key.Item2;

            // Find or create a group
            var existingGroup = analysis.SimilarFileGroups.FirstOrDefault(g =>
                g.FilePairs.Contains(file1) || g.FilePairs.Contains(file2));

            if (existingGroup != null)
            {
                // Add to existing group
                if (!existingGroup.FilePairs.Contains(file1))
                {
                    existingGroup.FilePairs.Add(file1);
                    existingGroup.FileCount++;
                    grouped.Add(file1);
                }

                if (!existingGroup.FilePairs.Contains(file2))
                {
                    existingGroup.FilePairs.Add(file2);
                    existingGroup.FileCount++;
                    grouped.Add(file2);
                }
            }
            else
            {
                // Create new group
                var newGroup = new SimilarFileGroup
                {
                    GroupName = $"Group {analysis.SimilarFileGroups.Count + 1}",
                    FileCount = 0,
                    FilePairs = new List<string>(),
                    CommonPattern = "Files with similar difference patterns"
                };

                if (!grouped.Contains(file1))
                {
                    newGroup.FilePairs.Add(file1);
                    newGroup.FileCount++;
                    grouped.Add(file1);
                }

                if (!grouped.Contains(file2))
                {
                    newGroup.FilePairs.Add(file2);
                    newGroup.FileCount++;
                    grouped.Add(file2);
                }

                if (newGroup.FileCount > 0)
                {
                    analysis.SimilarFileGroups.Add(newGroup);
                }
            }
        }

        // For each group, identify common patterns
        foreach (var group in analysis.SimilarFileGroups)
        {
            // Find patterns common to all files in the group
            HashSet<string> commonPatterns = null;

            foreach (var file in group.FilePairs)
            {
                var filePatterns = fileFingerprints[file];

                if (commonPatterns == null)
                {
                    commonPatterns = new HashSet<string>(filePatterns);
                }
                else
                {
                    commonPatterns.IntersectWith(filePatterns);
                }
            }

            if (commonPatterns != null && commonPatterns.Count > 0)
            {
                group.CommonPattern = $"{commonPatterns.Count} common difference pattern(s) including: " +
                                      string.Join(", ", commonPatterns.Take(3).Select(p => $"'{p}'"));
            }
        }

        // Add singleton groups for any files not grouped
        foreach (var file in fileFingerprints.Keys)
        {
            if (!grouped.Contains(file))
            {
                analysis.SimilarFileGroups.Add(new SimilarFileGroup
                {
                    GroupName = $"Group {analysis.SimilarFileGroups.Count + 1}",
                    FileCount = 1,
                    FilePairs = new List<string> { file },
                    CommonPattern = "Unique difference pattern"
                });
            }
        }
    }

    /// <summary>
    /// Force garbage collection to release memory
    /// </summary>
    private void ReleaseMemory()
    {
        // Force garbage collection
        GC.Collect(2, GCCollectionMode.Forced, true, true);
        GC.WaitForPendingFinalizers();
        GC.Collect(2, GCCollectionMode.Forced, true, true);

        // Log memory status
        long memoryUsed = GC.GetTotalMemory(false) / (1024 * 1024); // MB
        logger.LogDebug("Memory usage after GC: {MemoryUsed} MB", memoryUsed);
    }

    /// <summary>
    /// Adjust batch size based on file count and available memory
    /// </summary>
    private int AdjustBatchSize(int fileCount, int defaultBatchSize)
    {
        // For very large file sets, use smaller batches
        if (fileCount > 2000)
            return Math.Min(defaultBatchSize, 20);

        // For large file sets, use the default
        if (fileCount > 500)
            return defaultBatchSize;

        // For smaller file sets, can use larger batches
        if (fileCount > 100)
            return defaultBatchSize * 2;

        // For very small sets, use larger batches for better parallelism
        return Math.Max(fileCount, defaultBatchSize);
    }

    /// <summary>
    /// Normalize a property path by replacing array indices with wildcards
    /// and removing backing field notation
    /// </summary>
    private string NormalizePropertyPath(string propertyPath)
    {
        // Replace array indices with [*]
        var normalized = Regex.Replace(propertyPath, @"\[\d+\]", "[*]");

        // Remove backing fields
        normalized = Regex.Replace(normalized, @"<(\w+)>k__BackingField", "$1");

        return normalized;
    }
}