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
/// </summary>
public class ComparisonService : IComparisonService
{
    private readonly ILogger<ComparisonService> logger;
    private readonly IXmlDeserializationService deserializationService;
    private readonly IComparisonConfigurationService configService;
    private readonly IFileSystemService fileSystemService;
    private readonly PerformanceTracker _performanceTracker;

    private const int MaxConcurrentComparisons = 5; // Limit concurrent operations, todo: set via config?

    public ComparisonService(
        ILogger<ComparisonService> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        IFileSystemService fileSystemService,
        PerformanceTracker performanceTracker)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.fileSystemService = fileSystemService;
        this._performanceTracker = performanceTracker;
    }

    /// <summary>
    /// Compare two XML files using the specified domain model
    /// </summary>
    /// <param name="oldXmlStream">Stream containing the old/reference XML</param>
    /// <param name="newXmlStream">Stream containing the new/comparison XML</param>
    /// <param name="modelName">Name of the registered model to use for deserialization</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Comparison result with differences</returns>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        return await _performanceTracker.TrackOperationAsync("CompareXmlFilesAsync", async () =>
        {
            try
            {
                logger.LogInformation("Starting comparison of XML files using model {ModelName}", modelName);

                // Check if model exists (will throw if not found)
                var modelType = deserializationService.GetModelType(modelName);

                var deserializeMethod = typeof(IXmlDeserializationService)
                    .GetMethod(nameof(IXmlDeserializationService.DeserializeXml))
                    .MakeGenericMethod(modelType);

                var cloneMethod = typeof(IXmlDeserializationService)
                    .GetMethod(nameof(IXmlDeserializationService.CloneObject))
                    .MakeGenericMethod(modelType);

                // Call the methods via reflection with performance tracking
                object oldResponse = await _performanceTracker.TrackOperationAsync($"Deserialize_Old_{modelName}", () => 
                    Task.FromResult(deserializeMethod.Invoke(deserializationService, new object[] { oldXmlStream })));
                    
                object newResponse = await _performanceTracker.TrackOperationAsync($"Deserialize_New_{modelName}", () => 
                    Task.FromResult(deserializeMethod.Invoke(deserializationService, new object[] { newXmlStream })));

                object oldResponseCopy = _performanceTracker.TrackOperation($"Clone_Old_{modelName}", () => 
                    cloneMethod.Invoke(deserializationService, new object[] { oldResponse }));
                    
                object newResponseCopy = _performanceTracker.TrackOperation($"Clone_New_{modelName}", () => 
                    cloneMethod.Invoke(deserializationService, new object[] { newResponse }));

                // Get properties to ignore for normalization
                var propertiesToIgnore = await _performanceTracker.TrackOperationAsync("Get_Ignore_Rules", () => Task.FromResult(
                    configService.GetIgnoreRules()
                        .Where(r => r.IgnoreCompletely)
                        .Select(r => GetPropertyNameFromPath(r.PropertyPath))
                        .Where(p => !string.IsNullOrEmpty(p))
                        .Distinct()
                        .ToList()));

                // Normalize property values in both object graphs
                if (propertiesToIgnore.Any())
                {
                    await _performanceTracker.TrackOperationAsync("Normalize_Properties", async () => 
                    {
                        await Task.Run(() =>
                        {
                            configService.NormalizePropertyValues(oldResponseCopy, propertiesToIgnore);
                            configService.NormalizePropertyValues(newResponseCopy, propertiesToIgnore);
                        }, cancellationToken);
                    });
                }

                // Apply configured settings
                await _performanceTracker.TrackOperationAsync("Apply_Config_Settings", async () => 
                {
                    configService.ApplyConfiguredSettings();
                });

                // Compare the normalized objects
                var result = await _performanceTracker.TrackOperationAsync("Compare_Objects", async () => 
                {
                    return await Task.Run(() =>
                    {
                        var compareLogic = configService.GetCompareLogic();

                        var oldClone = cloneMethod.Invoke(deserializationService, new[] { oldResponseCopy });
                        var newClone = cloneMethod.Invoke(deserializationService, new[] { newResponseCopy });

                        return compareLogic.Compare(oldClone, newClone);
                    }, cancellationToken);
                });

                logger.LogInformation("Comparison completed. Found {DifferenceCount} differences",
                    result.Differences.Count);

                var filteredResult = FilterDuplicateDifferences(result);

                return filteredResult;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error occurred while comparing XML files");
                throw;
            }
        });
    }

    /// <summary>
    /// Compare multiple folder pairs of XML files
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder</param>
    /// <param name="folder2Files">List of files from the second folder</param>
    /// <param name="modelName">Name of the registered model to use for deserialization</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Results of comparing multiple files</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default)
    {
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
                logger.LogInformation("Comparing pair {PairNumber}/{TotalPairs}: {File1} vs {File2}", i + 1, pairCount, file1Path, file2Path);
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
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder</param>
    /// <param name="folder2Files">List of files from the second folder</param>
    /// <param name="modelName">Name of the registered model to use for deserialization</param>
    /// <param name="batchSize">Number of files to process in each batch</param>
    /// <param name="progress">Progress reporter</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Results of comparing multiple files</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 50,
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
    /// <param name="folderResult">Results of multiple file comparisons</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Analysis of patterns across compared files</returns>
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
                    if (analysis.TotalByCategory.ContainsKey(category.Key))
                    {
                        analysis.TotalByCategory[category.Key] += category.Value.Count;
                    }
                }

                // Process each difference
                foreach (var diff in filePair.Result.Differences)
                {
                    // Normalize the property path (remove indices, backing fields)
                    string normalizedPath = NormalizePropertyPath(diff.PropertyName);

                    // Create pattern info if not exists
                    if (!allPathPatterns.ContainsKey(normalizedPath))
                    {
                        allPathPatterns[normalizedPath] = new GlobalPatternInfo
                        {
                            PatternPath = normalizedPath,
                            OccurrenceCount = 0,
                            FileCount = 0
                        };
                    }

                    // Update pattern info
                    var patternInfo = allPathPatterns[normalizedPath];
                    patternInfo.OccurrenceCount++;
                    if (!patternInfo.AffectedFiles.Contains(pairIdentifier))
                    {
                        patternInfo.AffectedFiles.Add(pairIdentifier);
                        patternInfo.FileCount++;
                    }

                    // Add example if we don't have many
                    if (patternInfo.Examples.Count < 3)
                    {
                        patternInfo.Examples.Add(diff);
                    }

                    // Track property change info (create key from property + old value + new value)
                    var oldValue = diff.Object1Value?.ToString() ?? "null";
                    var newValue = diff.Object2Value?.ToString() ?? "null";
                    var changeKey = $"{normalizedPath}|{oldValue}|{newValue}";

                    if (!allPropertyChanges.ContainsKey(changeKey))
                    {
                        allPropertyChanges[changeKey] = new GlobalPropertyChangeInfo
                        {
                            PropertyName = normalizedPath,
                            OccurrenceCount = 0,
                            CommonChanges = new Dictionary<string, string>
                            {
                                { oldValue, newValue }
                            }
                        };
                    }

                    // Update property change info
                    var changeInfo = allPropertyChanges[changeKey];
                    changeInfo.OccurrenceCount++;
                    if (!changeInfo.AffectedFiles.Contains(pairIdentifier))
                    {
                        changeInfo.AffectedFiles.Add(pairIdentifier);
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
    /// Analyze semantic differences across multiple file comparison results
    /// </summary>
    /// <param name="folderResult">Results of multiple file comparisons</param>
    /// <param name="patternAnalysis">Pattern analysis of the comparison results</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>Semantic analysis of differences across compared files</returns>
    public async Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis patternAnalysis,
        CancellationToken cancellationToken = default)
    {
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
    /// Filter duplicate differences from a comparison result
    /// </summary>
    private ComparisonResult FilterDuplicateDifferences(ComparisonResult result)
    {
        if (result.Differences.Count <= 1)
            return result;

        // Group differences by their actual values that changed
        var uniqueDiffs = result.Differences
            .GroupBy(d => new
            {
                OldValue = d.Object1Value?.ToString() ?? "null",
                NewValue = d.Object2Value?.ToString() ?? "null"
            })
            .Select(group =>
            {
                // From each group, pick the simplest property path (one without backing fields)
                var bestMatch = group
                    .OrderBy(d => d.PropertyName.Contains("k__BackingField") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Length)
                    .First();

                return bestMatch;
            })
            .ToList();

        // Clear and replace the differences
        result.Differences.Clear();
        result.Differences.AddRange(uniqueDiffs);

        return result;
    }

    /// <summary>
    /// Force garbage collection to release memory
    /// </summary>
    private void ReleaseMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
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

    /// <summary>
    /// Extract the property name from a path
    /// </summary>
    private string GetPropertyNameFromPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
            return string.Empty;

        // If it's already a simple property name, return it
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
            return propertyPath;

        // Handle paths with array indices
        if (propertyPath.Contains("["))
        {
            // If it's something like Results[0].Score, extract Score
            var lastDotIndex = propertyPath.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < propertyPath.Length - 1)
                return propertyPath.Substring(lastDotIndex + 1);

            // If it's something like [0].Score, extract Score
            var lastBracketIndex = propertyPath.LastIndexOf(']');
            if (lastBracketIndex >= 0 && lastBracketIndex < propertyPath.Length - 2 &&
                propertyPath[lastBracketIndex + 1] == '.')
                return propertyPath.Substring(lastBracketIndex + 2);
        }

        // For paths like Body.Response.Results.Score, extract Score
        var parts = propertyPath.Split('.');
        return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
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
}