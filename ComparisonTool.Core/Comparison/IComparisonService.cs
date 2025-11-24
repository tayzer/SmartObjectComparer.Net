// <copyright file="IComparisonService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Interface for comparison execution.
/// </summary>
public interface IComparisonService {
    /// <summary>
    /// Compare two XML files using the specified domain model.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two XML files using the specified domain model with caching support.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<ComparisonResult> CompareXmlFilesWithCachingAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        string oldFilePath = null,
        string newFilePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON).
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<ComparisonResult> CompareFilesAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON) with caching support.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<ComparisonResult> CompareFilesWithCachingAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of XML files.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of XML files in batches with parallel processing.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 50,
        IProgress<(int Completed, int Total)> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze patterns across multiple file comparison results.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
        MultiFolderComparisonResult folderResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze semantic differences across multiple file comparison results.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis patternAnalysis,
        CancellationToken cancellationToken = default);

    Task<EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult>
        AnalyzeStructualPatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default);
}
