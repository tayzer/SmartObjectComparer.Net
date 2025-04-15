using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
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

    public ComparisonService(
        ILogger<ComparisonService> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
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

            // Call the methods via reflection
            var oldResponse = deserializeMethod.Invoke(deserializationService, new object[] { oldXmlStream });
            var newResponse = deserializeMethod.Invoke(deserializationService, new object[] { newXmlStream });

            var oldResponseCopy = cloneMethod.Invoke(deserializationService, new object[] { oldResponse });
            var newResponseCopy = cloneMethod.Invoke(deserializationService, new object[] { newResponse });

            // Get properties to ignore for normalization
            var propertiesToIgnore = configService.GetIgnoreRules()
                .Where(r => r.IgnoreCompletely)
                .Select(r => GetPropertyNameFromPath(r.PropertyPath))
                .Where(p => !string.IsNullOrEmpty(p))
                .Distinct()
                .ToList();

            // Normalize property values in both object graphs
            if (propertiesToIgnore.Any())
            {
                await Task.Run(() =>
                {
                    configService.NormalizePropertyValues(oldResponseCopy, propertiesToIgnore);
                    configService.NormalizePropertyValues(newResponseCopy, propertiesToIgnore);
                }, cancellationToken);
            }

            // Apply configured settings
            configService.ApplyConfiguredSettings();

            // Compare the normalized objects
            var result = await Task.Run(() =>
            {
                var compareLogic = configService.GetCompareLogic();
                return compareLogic.Compare(oldResponseCopy, newResponseCopy);
            }, cancellationToken);

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
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiFolderComparisonResult();

        // Determine how many pairs we can make
        int pairCount = Math.Min(folder1Files.Count, folder2Files.Count);
        result.TotalPairsCompared = pairCount;

        if (pairCount == 0)
        {
            logger.LogWarning("No file pairs to compare");
            return result;
        }

        logger.LogInformation("Starting comparison of {PairCount} file pairs using model {ModelName}",
            pairCount, modelName);

        // For each pair of files, compare them
        for (int i = 0; i < pairCount; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var (file1Stream, file1Name) = folder1Files[i];
            var (file2Stream, file2Name) = folder2Files[i];

            try
            {
                logger.LogInformation("Comparing pair {PairNumber}/{TotalPairs}: {File1} vs {File2}",
                    i + 1, pairCount, file1Name, file2Name);

                // Reset streams to beginning
                file1Stream.Position = 0;
                file2Stream.Position = 0;

                // Do the comparison
                var pairResult = await CompareXmlFilesAsync(
                    file1Stream,
                    file2Stream,
                    modelName,
                    cancellationToken);

                // Generate summary
                var categorizer = new DifferenceCategorizer();
                var summary = categorizer.CategorizeAndSummarize(pairResult);

                // Add to results
                var filePairResult = new FilePairComparisonResult
                {
                    File1Name = file1Name,
                    File2Name = file2Name,
                    Result = pairResult,
                    Summary = summary
                };

                result.FilePairResults.Add(filePairResult);

                // Update overall equality status
                if (!summary.AreEqual)
                {
                    result.AllEqual = false;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error comparing files {File1} and {File2}", file1Name, file2Name);
                throw;
            }
        }

        logger.LogInformation("Folder comparison completed. {EqualCount} equal, {DifferentCount} different",
            result.FilePairResults.Count(r => r.AreEqual),
            result.FilePairResults.Count(r => !r.AreEqual));

        return result;
    }

    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName,
        int batchSize = 50,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiFolderComparisonResult();
        result.TotalPairsCompared = Math.Min(folder1Files.Count, folder2Files.Count);

        // Process in batches
        for (int batchStart = 0; batchStart < result.TotalPairsCompared; batchStart += batchSize)
        {
            int currentBatchSize = Math.Min(batchSize, result.TotalPairsCompared - batchStart);

            var batchResult = await ProcessFileBatchAsync(
                folder1Files.Skip(batchStart).Take(currentBatchSize).ToList(),
                folder2Files.Skip(batchStart).Take(currentBatchSize).ToList(),
                modelName,
                cancellationToken);

            // Merge batch results into the final result
            result.FilePairResults.AddRange(batchResult.FilePairResults);
            result.AllEqual = result.AllEqual && batchResult.AllEqual;

            // Release resources after each batch
            GC.Collect();
        }

        return result;
    }

    private async Task<MultiFolderComparisonResult> ProcessFileBatchAsync(
     List<(Stream Stream, string FileName)> folder1Files,
     List<(Stream Stream, string FileName)> folder2Files,
     string modelName,
     CancellationToken cancellationToken = default)
    {
        try
        {
            var result = new MultiFolderComparisonResult();
            var filePairResults = new ConcurrentBag<FilePairComparisonResult>();

            // Use int instead of bool for atomic operations
            int equalityFlag = 1; // 1 means all equal so far

            // Calculate degree of parallelism (adjust based on system capabilities)
            int degreeOfParallelism = Math.Min(Environment.ProcessorCount, folder1Files.Count);

            // Create semaphore to limit concurrent operations
            using var semaphore = new SemaphoreSlim(degreeOfParallelism);

            // Create tasks for parallel execution
            var tasks = new List<Task>();

            for (int i = 0; i < folder1Files.Count; i++)
            {
                var index = i;
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await semaphore.WaitAsync(cancellationToken);

                        var (file1Stream, file1Name) = folder1Files[index];
                        var (file2Stream, file2Name) = folder2Files[index];

                        // Clone streams to avoid concurrent access issues
                        var file1Copy = new MemoryStream();
                        var file2Copy = new MemoryStream();

                        file1Stream.Position = 0;
                        file2Stream.Position = 0;

                        await file1Stream.CopyToAsync(file1Copy, cancellationToken);
                        await file2Stream.CopyToAsync(file2Copy, cancellationToken);

                        file1Copy.Position = 0;
                        file2Copy.Position = 0;

                        // Perform the comparison
                        var pairResult = await CompareXmlFilesAsync(
                            file1Copy,
                            file2Copy,
                            modelName,
                            cancellationToken);

                        // Generate summary using existing analyzer
                        var categorizer = new DifferenceCategorizer();
                        var summary = categorizer.CategorizeAndSummarize(pairResult);

                        // Create result
                        var filePairResult = new FilePairComparisonResult
                        {
                            File1Name = file1Name,
                            File2Name = file2Name,
                            Result = pairResult,
                            Summary = summary
                        };

                        filePairResults.Add(filePairResult);

                        // Update equality tracking using atomic operation
                        if (!summary.AreEqual)
                        {
                            Interlocked.Exchange(ref equalityFlag, 0);
                        }

                        // Clean up
                        await file1Copy.DisposeAsync();
                        await file2Copy.DisposeAsync();
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }, cancellationToken);

                tasks.Add(task);
            }

            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // Some tasks may have completed successfully, so we'll use what we have
                logger.LogWarning("Some comparison tasks were canceled");
            }

            // Populate the result with whatever we have
            result.FilePairResults = filePairResults.OrderBy(r => r.File1Name).ToList();
            result.AllEqual = equalityFlag == 1; // Convert back to boolean
            result.TotalPairsCompared = result.FilePairResults.Count;

            return result;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing file batch");
            throw;
        }
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

    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName,
        int batchSize = 50,
        IProgress<(int Completed, int Total)> progress = null,
        CancellationToken cancellationToken = default)
    {
        var result = new MultiFolderComparisonResult();
        result.TotalPairsCompared = Math.Min(folder1Files.Count, folder2Files.Count);
        int completedPairs = 0;

        // Process in batches
        for (int batchStart = 0; batchStart < result.TotalPairsCompared; batchStart += batchSize)
        {
            int currentBatchSize = Math.Min(batchSize, result.TotalPairsCompared - batchStart);

            var batchResult = await ProcessFileBatchAsync(
                folder1Files.Skip(batchStart).Take(currentBatchSize).ToList(),
                folder2Files.Skip(batchStart).Take(currentBatchSize).ToList(),
                modelName,
                cancellationToken);

            // Merge batch results into the final result
            result.FilePairResults.AddRange(batchResult.FilePairResults);
            result.AllEqual = result.AllEqual && batchResult.AllEqual;

            // Update progress
            completedPairs += currentBatchSize;
            progress?.Report((completedPairs, result.TotalPairsCompared));

            // Release resources after each batch
            GC.Collect();
        }

        return result;
    }
}