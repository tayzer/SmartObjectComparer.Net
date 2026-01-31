// <copyright file="ComparisonService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Serialization;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Configuration;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Serialization;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using KellermanSoftware.CompareNetObjects.TypeComparers;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Service responsible for executing comparisons between objects and handling comparison results.
/// </summary>
public class ComparisonService : IComparisonService
{
    // Concurrency control
    private const int MaxConcurrentComparisons = 5;
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger<ComparisonService> logger;
    private readonly IXmlDeserializationService deserializationService;
    private readonly DeserializationServiceFactory deserializationFactory;
    private readonly IComparisonConfigurationService configService;
    private readonly IFileSystemService fileSystemService;
    private readonly PerformanceTracker performanceTracker;
    private readonly SystemResourceMonitor resourceMonitor;
    private readonly ComparisonResultCacheService cacheService;
    private readonly IComparisonEngine comparisonEngine;
    private readonly IComparisonOrchestrator comparisonOrchestrator;

    public ComparisonService(
        ILogger<ComparisonService> logger,
        IXmlDeserializationService deserializationService,
        IComparisonConfigurationService configService,
        IFileSystemService fileSystemService,
        PerformanceTracker performanceTracker,
        SystemResourceMonitor resourceMonitor,
        ComparisonResultCacheService cacheService,
        IComparisonEngine comparisonEngine,
        IComparisonOrchestrator comparisonOrchestrator,
        DeserializationServiceFactory? deserializationFactory = null)
    {
        this.logger = logger;
        this.deserializationService = deserializationService;
        this.configService = configService;
        this.fileSystemService = fileSystemService;
        this.performanceTracker = performanceTracker;
        this.resourceMonitor = resourceMonitor;
        this.cacheService = cacheService;
        this.comparisonEngine = comparisonEngine;
        this.comparisonOrchestrator = comparisonOrchestrator;
        this.deserializationFactory = deserializationFactory;
    }

    /// <summary>
    /// Compare two XML files using the specified domain model with caching and performance optimization.
    /// </summary>
    /// <param name="oldXmlStream">Stream containing the old/reference XML.</param>
    /// <param name="newXmlStream">Stream containing the new/comparison XML.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="oldFilePath">Path to the old file (for logging).</param>
    /// <param name="newFilePath">Path to the new file (for logging).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    public async Task<ComparisonResult> CompareXmlFilesWithCachingAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        string oldFilePath = null,
        string newFilePath = null,
        CancellationToken cancellationToken = default)
    {
        var result = await comparisonOrchestrator.CompareXmlFilesWithCachingAsync(oldXmlStream, newXmlStream, modelName, oldFilePath, newFilePath, cancellationToken).ConfigureAwait(false);

        // Filter duplicate differences (e.g., System.Collections.IList.Item vs standard indexed paths)
        return ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, logger);
    }

    /// <summary>
    /// Compare two XML files using the specified domain model (legacy method without caching).
    /// </summary>
    /// <param name="oldXmlStream">Stream containing the old/reference XML.</param>
    /// <param name="newXmlStream">Stream containing the new/comparison XML.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    public async Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        var result = await comparisonOrchestrator.CompareXmlFilesAsync(oldXmlStream, newXmlStream, modelName, cancellationToken).ConfigureAwait(false);

        // Ensure duplicate differences are removed before returning
        return ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, logger);
    }

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON).
    /// </summary>
    /// <param name="oldFileStream">Stream containing the old/reference file.</param>
    /// <param name="newFileStream">Stream containing the new/comparison file.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="oldFilePath">Path to the old file (for logging and format detection).</param>
    /// <param name="newFilePath">Path to the new file (for logging and format detection).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    public async Task<ComparisonResult> CompareFilesWithCachingAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default)
    {
        var result = await comparisonOrchestrator.CompareFilesWithCachingAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken).ConfigureAwait(false);

        // Post-process to remove duplicate representation differences
        return ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, logger);
    }

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON) - legacy method without caching.
    /// </summary>
    /// <param name="oldFileStream">Stream containing the old/reference file.</param>
    /// <param name="newFileStream">Stream containing the new/comparison file.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="oldFilePath">Path to the old file (for format detection).</param>
    /// <param name="newFilePath">Path to the new file (for format detection).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    public async Task<ComparisonResult> CompareFilesAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default)
    {
        var result = await comparisonOrchestrator.CompareFilesAsync(oldFileStream, newFileStream, modelName, oldFilePath, newFilePath, cancellationToken).ConfigureAwait(false);

        // Remove duplicate differences so the UI sees a concise list
        return ComparisonTool.Core.Comparison.Utilities.DifferenceFilter.FilterDuplicateDifferences(result, logger);
    }

    /// <summary>
    /// Compare multiple folder pairs of XML files.
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder.</param>
    /// <param name="folder2Files">List of files from the second folder.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Results of comparing multiple files.</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default)
    {
        return await comparisonOrchestrator.CompareFoldersAsync(folder1Files, folder2Files, modelName, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Compare multiple folder pairs of XML files in batches with parallel processing.
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder.</param>
    /// <param name="folder2Files">List of files from the second folder.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="batchSize">Number of files to process in each batch.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Results of comparing multiple files.</returns>
    public async Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 50,
        IProgress<(int Completed, int Total)>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return await comparisonOrchestrator.CompareFoldersInBatchesAsync(folder1Files, folder2Files, modelName, batchSize, progress, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Analyze patterns across multiple file comparison results.
    /// </summary>
    /// <param name="folderResult">Results of multiple file comparisons.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Analysis of patterns across compared files.</returns>
    public async Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
      MultiFolderComparisonResult folderResult,
      CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                var overallSw = System.Diagnostics.Stopwatch.StartNew();
                logger.LogInformation(
                    "Starting pattern analysis of {FileCount} comparison results",
                    folderResult.FilePairResults.Count);

                var analysis = new ComparisonPatternAnalysis
                {
                    TotalFilesPaired = folderResult.TotalPairsCompared,
                    FilesWithDifferences = folderResult.FilePairResults.Count(r => !r.AreEqual),
                    TotalDifferences = folderResult.FilePairResults.Sum(r => r.Summary?.TotalDifferenceCount ?? 0),
                };

                // Initialize category counts
                foreach (DifferenceCategory category in Enum.GetValues(typeof(DifferenceCategory)))
                {
                    analysis.TotalByCategory[category] = 0;
                }

                var allPathPatterns = new ConcurrentDictionary<string, GlobalPatternInfo>(System.StringComparer.Ordinal);
                var allPropertyChanges = new ConcurrentDictionary<string, GlobalPropertyChangeInfo>(System.StringComparer.Ordinal);
                var categoryCounts = new ConcurrentDictionary<DifferenceCategory, int>();

                var phaseSw = System.Diagnostics.Stopwatch.StartNew();

                // Parallelize over file pairs
                Parallel.ForEach(folderResult.FilePairResults, new ParallelOptions { CancellationToken = cancellationToken }, filePair =>
                {
                    if (filePair.AreEqual)
                    {
                        return;
                    }

                    var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";

                    // Category counts (guard Summary/result which may be nullable)
                    foreach (var category in filePair.Summary?.DifferencesByChangeType ?? new Dictionary<DifferenceCategory, IList<Difference>>())
                    {
                        categoryCounts.AddOrUpdate(category.Key, category.Value.Count, (k, v) => v + category.Value.Count);
                    }

                    var differences = filePair.Result?.Differences ?? new List<Difference>();
                    foreach (var diff in differences)
                    {
                        var normalizedPath = NormalizePropertyPath(diff.PropertyName);

                        // Path pattern aggregation
                        var patternInfo = allPathPatterns.GetOrAdd(normalizedPath, _ => new GlobalPatternInfo
                        {
                            PatternPath = normalizedPath,
                            OccurrenceCount = 0,
                            FileCount = 0,
                        });
                        Interlocked.Increment(ref patternInfo.OccurrenceCountRef);
                        lock (patternInfo.AffectedFiles)
                        {
                            if (!patternInfo.AffectedFiles.Contains(pairIdentifier, System.StringComparer.Ordinal))
                            {
                                patternInfo.AffectedFiles.Add(pairIdentifier);
                                patternInfo.FileCount++;
                            }
                        }

                        lock (patternInfo.Examples)
                        {
                            if (patternInfo.Examples.Count < 3)
                            {
                                patternInfo.Examples.Add(diff);
                            }
                        }

                        // Property change aggregation
                        var oldValue = FormatValue(diff.Object1Value);
                        var newValue = FormatValue(diff.Object2Value);
                        var changeKey = $"{normalizedPath}|{oldValue}|{newValue}";
                        var changeInfo = allPropertyChanges.GetOrAdd(changeKey, _ => new GlobalPropertyChangeInfo
                        {
                            PropertyName = normalizedPath,
                            OccurrenceCount = 0,
                            CommonChanges = new Dictionary<string, string>(System.StringComparer.Ordinal) { { oldValue, newValue } },
                        });
                        Interlocked.Increment(ref changeInfo.OccurrenceCountRef);
                        lock (changeInfo.AffectedFiles)
                        {
                            if (!changeInfo.AffectedFiles.Contains(pairIdentifier, System.StringComparer.Ordinal))
                            {
                                changeInfo.AffectedFiles.Add(pairIdentifier);
                            }
                        }
                    }
                });
                logger.LogInformation("[TIMING] Parallel pattern aggregation took {ElapsedMs} ms", phaseSw.ElapsedMilliseconds);

                // Copy category counts to analysis
                foreach (var kvp in categoryCounts)
                {
                    analysis.TotalByCategory[kvp.Key] = kvp.Value;
                }

                var sortSw = System.Diagnostics.Stopwatch.StartNew();

                // Sort and select most common patterns
                analysis.CommonPathPatterns = allPathPatterns.Values
                    .Where(p => p.FileCount > 1)
                    .OrderByDescending(p => p.FileCount)
                    .ThenByDescending(p => p.OccurrenceCount)
                    .Take(20)
                    .ToList();

                // Sort and select most common property changes
                analysis.CommonPropertyChanges = allPropertyChanges.Values
                    .Where(c => c.AffectedFiles.Count > 1)
                    .OrderByDescending(c => c.AffectedFiles.Count)
                    .ThenByDescending(c => c.OccurrenceCount)
                    .Take(20)
                    .ToList();
                logger.LogInformation("[TIMING] Sorting and selection took {ElapsedMs} ms", sortSw.ElapsedMilliseconds);

                // Group similar files based on their difference patterns
                var groupSw = System.Diagnostics.Stopwatch.StartNew();
                GroupSimilarFiles(folderResult, analysis);
                logger.LogInformation("[TIMING] Grouping similar files took {ElapsedMs} ms", groupSw.ElapsedMilliseconds);

                logger.LogInformation(
                    "Pattern analysis completed. Found {PatternCount} common patterns across files. Total time: {TotalMs} ms",
                    analysis.CommonPathPatterns.Count,
                    overallSw.ElapsedMilliseconds);

                return analysis;
            }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Analyze semantic differences across multiple file comparison results.
    /// </summary>
    /// <param name="folderResult">Results of multiple file comparisons.</param>
    /// <param name="patternAnalysis">Pattern analysis of the comparison results.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Semantic analysis of differences across compared files.</returns>
    public async Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis patternAnalysis,
        CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
            {
                logger.LogInformation("Starting semantic difference analysis");

                var analyzer = new SemanticDifferenceAnalyzer(folderResult, patternAnalysis);
                var semanticAnalysis = analyzer.AnalyzeSemanticGroups();

                logger.LogInformation(
                    "Semantic analysis completed. Found {GroupCount} semantic groups with {DifferenceCount} differences",
                    semanticAnalysis.SemanticGroups.Count,
                    semanticAnalysis.CategorizedDifferences);

                return semanticAnalysis;
            }, cancellationToken).ConfigureAwait(false);
    }

    public async Task<EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult>
        AnalyzeStructualPatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default)
    {
        return await Task.Run(
            () =>
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
            }, cancellationToken).ConfigureAwait(false);
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }

    /// <summary>
    /// Group similar files based on their difference patterns using a hybrid (exact + MinHash/LSH) approach.
    /// </summary>
    private void GroupSimilarFiles(MultiFolderComparisonResult folderResult, ComparisonPatternAnalysis analysis)
    {
        if (analysis.FilesWithDifferences <= 1)
        {
            return;
        }

        // Step 1: Build fingerprints
        var fileFingerprints = new Dictionary<string, HashSet<string>>(System.StringComparer.Ordinal);
        foreach (var filePair in folderResult.FilePairResults)
        {
            if (filePair.AreEqual)
            {
                continue;
            }

            var pairIdentifier = $"{filePair.File1Name} vs {filePair.File2Name}";
            var fingerprint = new HashSet<string>(System.StringComparer.Ordinal);
            var fpDifferences = filePair.Result?.Differences ?? new System.Collections.Generic.List<KellermanSoftware.CompareNetObjects.Difference>();
            foreach (var diff in fpDifferences)
            {
                fingerprint.Add(NormalizePropertyPath(diff.PropertyName));
            }

            fileFingerprints[pairIdentifier] = fingerprint;
        }

        // Step 2: Group by exact signature
        var signatureToFiles = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);
        var fileToSignature = new Dictionary<string, string>(System.StringComparer.Ordinal);
        foreach (var kvp in fileFingerprints)
        {
            var signature = string.Join("|", kvp.Value.OrderBy(x => x, System.StringComparer.Ordinal));
            if (!signatureToFiles.ContainsKey(signature))
            {
                signatureToFiles[signature] = new List<string>();
            }

            signatureToFiles[signature].Add(kvp.Key);
            fileToSignature[kvp.Key] = signature;
        }

        var grouped = new HashSet<string>(System.StringComparer.Ordinal);

        // Add exact groups
        foreach (var group in signatureToFiles.Values)
        {
            if (group.Count > 1)
            {
                analysis.SimilarFileGroups.Add(new SimilarFileGroup
                {
                    GroupName = $"Group {analysis.SimilarFileGroups.Count + 1}",
                    FileCount = group.Count,
                    FilePairs = group.ToList(),
                    CommonPattern = $"Identical difference pattern ({group.Count} files)",
                });
                foreach (var f in group)
                {
                    grouped.Add(f);
                }
            }
        }

        // Step 3: Fuzzy grouping with MinHash + LSH
        var minHasher = new ComparisonTool.Core.Comparison.Analysis.MinHash(64);
        var minhashSigs = new Dictionary<string, int[]>(System.StringComparer.Ordinal);
        foreach (var kvp in fileFingerprints)
        {
            if (!grouped.Contains(kvp.Key))
            {
                minhashSigs[kvp.Key] = minHasher.ComputeSignature(kvp.Value);
            }
        }

        // LSH: bucket by first K hash values
        int lshBands = 8, bandSize = 8; // 8 bands of 8 hashes each
        var lshBuckets = new Dictionary<string, List<string>>(System.StringComparer.Ordinal);
        foreach (var kvp in minhashSigs)
        {
            var bucketKey = string.Join("-", kvp.Value
                .Take(lshBands * bandSize)
                .Select((v, i) => i % bandSize == 0 ? v.ToString(CultureInfo.InvariantCulture) : null)
                .Where(x => x != null));
            if (!lshBuckets.ContainsKey(bucketKey))
            {
                lshBuckets[bucketKey] = new List<string>();
            }

            lshBuckets[bucketKey].Add(kvp.Key);
        }

        // For each bucket, group files with high estimated Jaccard
        var used = new HashSet<string>(System.StringComparer.Ordinal);
        foreach (var bucket in lshBuckets.Values)
        {
            if (bucket.Count < 2)
            {
                continue;
            }

            var group = new List<string>();
            for (var i = 0; i < bucket.Count; i++)
            {
                if (used.Contains(bucket[i]))
                {
                    continue;
                }

                group.Clear();
                group.Add(bucket[i]);
                used.Add(bucket[i]);
                var sig1 = minhashSigs[bucket[i]];
                for (var j = i + 1; j < bucket.Count; j++)
                {
                    if (used.Contains(bucket[j]))
                    {
                        continue;
                    }

                    var sig2 = minhashSigs[bucket[j]];
                    var estJaccard = minHasher.EstimateJaccard(sig1, sig2);
                    if (estJaccard >= 0.6)
                    {
                        group.Add(bucket[j]);
                        used.Add(bucket[j]);
                    }
                }

                if (group.Count > 1)
                {
                    analysis.SimilarFileGroups.Add(new SimilarFileGroup
                    {
                        GroupName = $"Group {analysis.SimilarFileGroups.Count + 1}",
                        FileCount = group.Count,
                        FilePairs = group.ToList(),
                        CommonPattern = $"Fuzzy-similar difference pattern ({group.Count} files, est. Jaccard ≥ 0.6)",
                    });
                }
            }
        }

        // Step 4: Add singletons
        foreach (var file in fileFingerprints.Keys)
        {
            if (!grouped.Contains(file) && !used.Contains(file))
            {
                analysis.SimilarFileGroups.Add(new SimilarFileGroup
                {
                    GroupName = $"Group {analysis.SimilarFileGroups.Count + 1}",
                    FileCount = 1,
                    FilePairs = new List<string> { file },
                    CommonPattern = "Unique difference pattern",
                });
            }
        }
    }

    /// <summary>
    /// Filter duplicate differences from a comparison result.
    /// </summary>
    private ComparisonResult FilterDuplicateDifferences(ComparisonResult result)
    {
        if (result.Differences.Count <= 1)
        {
            return result;
        }

        var originalCount = result.Differences.Count;
        logger.LogDebug("Filtering duplicate differences. Original count: {OriginalCount}", originalCount);

        // Log all differences for debugging
        foreach (var diff in result.Differences)
        {
            if (diff.PropertyName.Contains("Residents"))
            {
                logger.LogDebug(
                    "Found Residents difference: '{PropertyName}' (Old: '{OldValue}', New: '{NewValue}')",
                    diff.PropertyName,
                    diff.Object1Value,
                    diff.Object2Value);
            }
        }

        // Log all differences for debugging (all of them)
        logger.LogDebug("=== ALL DIFFERENCES ===");
        foreach (var diff in result.Differences)
        {
            logger.LogDebug(
                "DIFFERENCE: '{PropertyName}' (Old: '{OldValue}', New: '{NewValue}')",
                diff.PropertyName,
                diff.Object1Value,
                diff.Object2Value);
        }

        logger.LogDebug("=== END ALL DIFFERENCES ===");

        // Filter out confusing collection count differences and improve null element differences
        var filteredDifferences = result.Differences.Where(diff => !IsConfusingCollectionDifference(diff)).ToList();

        // Improve null element differences to be more descriptive
        var improvedDifferences = filteredDifferences.Select(diff => ImproveDifferenceDescription(diff)).ToList();

        // Group differences using the new grouping key that handles System.Collections paths properly
        var groups = improvedDifferences.GroupBy(d => CreateGroupingKey(d)).ToList();

        logger.LogDebug("=== GROUPING RESULTS ===");
        foreach (var group in groups)
        {
            logger.LogDebug(
                "GROUP: Path='{PropertyPath}', OldValue='{OldValue}', NewValue='{NewValue}', Count={Count}",
                group.Key.PropertyPath,
                group.Key.OldValue,
                group.Key.NewValue,
                group.Count());
            foreach (var item in group)
            {
                logger.LogDebug("  - {PropertyName}", item.PropertyName);
            }
        }

        logger.LogDebug("=== END GROUPING RESULTS ===");

        var uniqueDiffs = groups
            .Select(group =>
            {
                // From each group, pick the best property path
                // Prefer standard array notation over System.Collections notation
                var bestMatch = group
                    .OrderBy(d => d.PropertyName.Contains("k__BackingField") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Contains("System.Collections.IList.Item") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Contains("System.Collections.Generic.IList`1.Item") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Length)
                    .First();

                if (group.Count() > 1)
                {
                    logger.LogDebug(
                        "Found duplicate group with {Count} items. Property path: {PropertyPath}. Selected: {SelectedPath}",
                        group.Count(),
                        group.Key.PropertyPath,
                        bestMatch.PropertyName);
                    foreach (var item in group)
                    {
                        logger.LogDebug(
                            "  - {PropertyName} (Old: {OldValue}, New: {NewValue})",
                            item.PropertyName,
                            item.Object1Value,
                            item.Object2Value);
                    }
                }
                else if (group.Key.PropertyPath.Contains("Residents"))
                {
                    logger.LogDebug(
                        "Single Residents difference: {PropertyName} (Old: {OldValue}, New: {NewValue})",
                        bestMatch.PropertyName,
                        group.Key.OldValue,
                        group.Key.NewValue);
                }

                // Log all Residents groups for debugging
                if (group.Key.PropertyPath.Contains("Residents"))
                {
                    logger.LogDebug(
                        "Residents group: {Count} items, Path: {PropertyPath}",
                        group.Count(),
                        group.Key.PropertyPath);
                }

                return bestMatch;
            })
            .ToList();

        var filteredCount = uniqueDiffs.Count;
        logger.LogDebug(
            "Duplicate filtering complete. Original: {OriginalCount}, Filtered: {FilteredCount}, Removed: {RemovedCount}",
            originalCount,
            filteredCount,
            originalCount - filteredCount);

        // Clear and replace the differences
        result.Differences.Clear();
        result.Differences.AddRange(uniqueDiffs);

        return result;
    }

    /// <summary>
    /// Force garbage collection to release memory.
    /// </summary>
    private void ReleaseMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    /// <summary>
    /// Normalize a property path by replacing array indices with wildcards
    /// and removing backing field notation.
    /// </summary>
    private string NormalizePropertyPath(string propertyPath)
    {
        var normalized = PropertyPathNormalizer.NormalizePropertyPath(propertyPath, logger);

        // Special debug logging for the specific paths mentioned in the issue
        if (propertyPath.Contains("System.Collections.IList.Item") || propertyPath.Contains("Residents"))
        {
            logger.LogDebug("Processing path with System.Collections or Residents: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }

        // Test normalization for the specific case mentioned in the issue
        if (propertyPath.Contains("Result.Report.Applicant") && propertyPath.Contains("Residents"))
        {
            logger.LogDebug("Found Residents path: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }

        // Test the specific paths mentioned in the issue
        if (string.Equals(propertyPath, "Result.Report.Applicant[0].Addresses[1].Residents.System.Collections.IList.Item[0]", StringComparison.Ordinal))
        {
            logger.LogDebug("TESTING: System.Collections path: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }
        else if (string.Equals(propertyPath, "Result.Report.Applicant[0].Addresses[1].Residents[0]", StringComparison.Ordinal))
        {
            logger.LogDebug("TESTING: Standard path: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }

        // Test the paths from the actual issue
        if (propertyPath.Contains("TestThisThing") && propertyPath.Contains("TestObjects") && !propertyPath.Contains("System.Collections"))
        {
            logger.LogDebug("TESTING: Standard TestObjects path: '{Original}' -> '{Normalized}'", propertyPath, normalized);
        }

        return normalized;
    }

    /// <summary>
    /// Create a grouping key that normalizes System.Collections paths but preserves property distinctions.
    /// </summary>
    private DifferenceGroupingKey CreateGroupingKey(Difference diff)
    {
        var normalizedPath = NormalizePropertyPath(diff.PropertyName);

        // Always use the normalized path for grouping to ensure System.Collections paths are grouped with their standard equivalents
        // The normalization handles both System.Collections paths and standard array paths consistently
        var groupingPath = normalizedPath;

        // Add debug logging for the specific case mentioned in the issue
        if (diff.PropertyName.Contains("Residents"))
        {
            var isSystemCollections = PropertyPathNormalizer.ContainsSystemCollections(diff.PropertyName);
            logger.LogDebug(
                "Creating grouping key for Residents path: '{Original}' -> '{Normalized}' -> '{GroupingPath}' (IsSystemCollections: {IsSystemCollections})",
                diff.PropertyName,
                normalizedPath,
                groupingPath,
                isSystemCollections);
        }

        return new DifferenceGroupingKey
        {
            OldValue = FormatValue(diff.Object1Value),
            NewValue = FormatValue(diff.Object2Value),
            PropertyPath = groupingPath,
        };
    }

    /// <summary>
    /// Extract the property name from a path.
    /// </summary>
    private string GetPropertyNameFromPath(string propertyPath)
    {
        if (string.IsNullOrEmpty(propertyPath))
        {
            return string.Empty;
        }

        // If it's already a simple property name, return it
        if (!propertyPath.Contains(".") && !propertyPath.Contains("["))
        {
            return propertyPath;
        }

        // Handle paths with array indices
        if (propertyPath.Contains("["))
        {
            // If it's something like Results[0].Score, extract Score
            var lastDotIndex = propertyPath.LastIndexOf('.');
            if (lastDotIndex >= 0 && lastDotIndex < propertyPath.Length - 1)
            {
                return propertyPath.Substring(lastDotIndex + 1);
            }

            // If it's something like [0].Score, extract Score
            var lastBracketIndex = propertyPath.LastIndexOf(']');
            if (lastBracketIndex >= 0 && lastBracketIndex < propertyPath.Length - 2 &&
                propertyPath[lastBracketIndex + 1] == '.')
            {
                return propertyPath.Substring(lastBracketIndex + 2);
            }
        }

        // For paths like Body.Response.Results.Score, extract Score
        var parts = propertyPath.Split('.');
        return parts.Length > 0 ? parts[parts.Length - 1] : string.Empty;
    }

    /// <summary>
    /// Check if a difference represents a confusing collection count difference that should be filtered out.
    /// </summary>
    private bool IsConfusingCollectionDifference(Difference diff)
    {
        // Filter out collection count differences that just show the count without context
        if (diff.PropertyName.EndsWith(".System.Collections.IList.Item", StringComparison.Ordinal) ||
            diff.PropertyName.EndsWith(".System.Collections.Generic.IList`1.Item", StringComparison.Ordinal))
        {
            var value1Text = FormatValue(diff.Object1Value);
            var value2Text = FormatValue(diff.Object2Value);

            // Check if this is just a count difference (old and new values are numbers)
            if (int.TryParse(value1Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) &&
                int.TryParse(value2Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                logger.LogDebug(
                    "Filtering out confusing collection count difference: '{PropertyName}' (Old: '{OldValue}', New: '{NewValue}')",
                    diff.PropertyName,
                    diff.Object1Value,
                    diff.Object2Value);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Improve the description of differences to be more user-friendly.
    /// </summary>
    private Difference ImproveDifferenceDescription(Difference diff)
    {
        // Handle null element differences to be more descriptive
        var oldValueText = FormatValue(diff.Object1Value);
        var newValueText = FormatValue(diff.Object2Value);

        if (diff.PropertyName.Contains(".System.Collections.IList.Item[", StringComparison.Ordinal) &&
            string.Equals(oldValueText, "(null)", StringComparison.Ordinal) &&
            newValueText.Contains(".", StringComparison.Ordinal))
        {
            // Extract the index from the property path
            var indexMatch = System.Text.RegularExpressions.Regex.Match(diff.PropertyName, @"\[(\d+)\]$", RegexOptions.ExplicitCapture, RegexTimeout);
            if (indexMatch.Success)
            {
                var index = indexMatch.Groups[1].Value;
                var basePath = diff.PropertyName.Replace($".System.Collections.IList.Item[{index}]", string.Empty);

                // Create a more descriptive property name
                var improvedPropertyName = $"{basePath}[{index}] (New Element)";

                logger.LogDebug(
                    "Improving null element difference: '{Original}' -> '{Improved}'",
                    diff.PropertyName,
                    improvedPropertyName);

                return new Difference
                {
                    PropertyName = improvedPropertyName,
                    Object1Value = diff.Object1Value,
                    Object2Value = diff.Object2Value,
                };
            }
        }

        return diff;
    }

    /// <summary>
    /// Adjust batch size based on file count and available memory.
    /// </summary>
    private int AdjustBatchSize(int fileCount, int defaultBatchSize)
    {
        // For very large file sets, use smaller batches
        if (fileCount > 2000)
        {
            return Math.Min(defaultBatchSize, 20);
        }

        // For large file sets, use the default
        if (fileCount > 500)
        {
            return defaultBatchSize;
        }

        // For smaller file sets, can use larger batches
        if (fileCount > 100)
        {
            return defaultBatchSize * 2;
        }

        // For very small sets, use larger batches for better parallelism
        return Math.Max(fileCount, defaultBatchSize);
    }

    /// <summary>
    /// Calculate optimal parallelism based on file count, file sizes, and system resources.
    /// </summary>
    private int CalculateOptimalParallelism(int fileCount, IEnumerable<string>? sampleFiles = null)
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

    /// <summary>
    /// Calculate optimal batch size based on file count, system resources, and estimated file size.
    /// </summary>
    private int CalculateOptimalBatchSize(int fileCount, IEnumerable<string>? sampleFiles = null)
    {
        // For very small sets, use a single batch
        if (fileCount < 10)
        {
            return fileCount;
        }

        // Use the resource monitor to determine optimal batch size
        long averageFileSizeKb = 0;

        // If sample files were provided, estimate average file size
        if (sampleFiles != null)
        {
            averageFileSizeKb = performanceTracker.TrackOperation("Calculate_Avg_FileSize", () =>
                resourceMonitor.CalculateAverageFileSizeKb(sampleFiles.Take(Math.Min(20, fileCount))));
        }

        return resourceMonitor.CalculateOptimalBatchSize(fileCount, averageFileSizeKb);
    }

    /// <summary>
    /// Represents a key for grouping differences.
    /// </summary>
    private class DifferenceGroupingKey
    {
        required public string OldValue
        {
            get; set;
        }

        required public string NewValue
        {
            get; set;
        }

        required public string PropertyPath
        {
            get; set;
        }

        public override bool Equals(object obj)
        {
            if (obj is not DifferenceGroupingKey other)
            {
                return false;
            }

            return string.Equals(OldValue, other.OldValue, StringComparison.Ordinal) &&
                   string.Equals(NewValue, other.NewValue, StringComparison.Ordinal) &&
                   string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal);
        }

        public override int GetHashCode()
        {
            var comparer = System.StringComparer.Ordinal;
            return HashCode.Combine(
                comparer.GetHashCode(OldValue ?? string.Empty),
                comparer.GetHashCode(NewValue ?? string.Empty),
                comparer.GetHashCode(PropertyPath ?? string.Empty));
        }
    }
}
