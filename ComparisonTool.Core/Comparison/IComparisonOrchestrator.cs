// <copyright file="IComparisonOrchestrator.cs" company="PlaceholderCompany">
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Interface for the comparison orchestrator that handles file-level comparison operations.
/// </summary>
public interface IComparisonOrchestrator {
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
    Task<ComparisonResult> CompareXmlFilesWithCachingAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two XML files using the specified domain model.
    /// </summary>
    /// <param name="oldXmlStream">Stream containing the old/reference XML.</param>
    /// <param name="newXmlStream">Stream containing the new/comparison XML.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    Task<ComparisonResult> CompareXmlFilesAsync(
        Stream oldXmlStream,
        Stream newXmlStream,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON) with caching.
    /// </summary>
    /// <param name="oldFileStream">Stream containing the old/reference file.</param>
    /// <param name="newFileStream">Stream containing the new/comparison file.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="oldFilePath">Path to the old file (for logging and format detection).</param>
    /// <param name="newFilePath">Path to the new file (for logging and format detection).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    Task<ComparisonResult> CompareFilesWithCachingAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare two files with auto-format detection (supports XML and JSON).
    /// </summary>
    /// <param name="oldFileStream">Stream containing the old/reference file.</param>
    /// <param name="newFileStream">Stream containing the new/comparison file.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="oldFilePath">Path to the old file (for format detection).</param>
    /// <param name="newFilePath">Path to the new file (for format detection).</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Comparison result with differences.</returns>
    Task<ComparisonResult> CompareFilesAsync(
        Stream oldFileStream,
        Stream newFileStream,
        string modelName,
        string oldFilePath,
        string newFilePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of files.
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder.</param>
    /// <param name="folder2Files">List of files from the second folder.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Results of comparing multiple files.</returns>
    Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Compare multiple folder pairs of files in batches with parallel processing.
    /// </summary>
    /// <param name="folder1Files">List of files from the first folder.</param>
    /// <param name="folder2Files">List of files from the second folder.</param>
    /// <param name="modelName">Name of the registered model to use for deserialization.</param>
    /// <param name="batchSize">Number of files to process in each batch.</param>
    /// <param name="progress">Progress reporter.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>Results of comparing multiple files.</returns>
    Task<MultiFolderComparisonResult> CompareFoldersInBatchesAsync(
        List<string> folder1Files,
        List<string> folder2Files,
        string modelName,
        int batchSize = 25,
        IProgress<(int Completed, int Total)> progress = null,
        CancellationToken cancellationToken = default);
}
