using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Interface for comparison execution
/// </summary>
public interface IComparisonService
{
    /// <summary>
    /// Compare two XML files using the specified domain model
    /// </summary>
    Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two XML files using the specified domain model with caching support
    /// </summary>
    Task<ComparisonResult> CompareXmlFilesWithCachingAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        string oldFilePath = null,
        string newFilePath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON)
    /// </summary>
    Task<ComparisonResult> CompareFilesAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON) with caching support
    /// </summary>
    Task<ComparisonResult> CompareFilesWithCachingAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of XML files
    /// </summary>
    Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of XML files in batches with parallel processing
    /// </summary>
    Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 50,
        IProgress<(int Completed, int Total)> progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze patterns across multiple file comparison results
    /// </summary>
    Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
        MultiFolderComparisonResult folderResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze semantic differences across multiple file comparison results
    /// </summary>
    Task<SemanticDifferenceAnalysis> AnalyzeSemanticDifferencesAsync(
        MultiFolderComparisonResult folderResult,
        ComparisonPatternAnalysis patternAnalysis,
        CancellationToken cancellationToken = default);

    Task<EnhancedStructuralDifferenceAnalyzer.EnhancedStructuralAnalysisResult>
        AnalyzeStructualPatternsAsync(
            MultiFolderComparisonResult folderResult,
            CancellationToken cancellationToken = default);
}