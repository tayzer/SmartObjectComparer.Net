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
    /// Compare multiple folder pairs of XML files
    /// </summary>
    Task<MultiFolderComparisonResult> CompareFoldersAsync(
        List<(Stream Stream, string FileName)> folder1Files,
        List<(Stream Stream, string FileName)> folder2Files,
        string modelName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze patterns across multiple file comparison results
    /// </summary>
    Task<ComparisonPatternAnalysis> AnalyzePatternsAsync(
        MultiFolderComparisonResult folderResult,
        CancellationToken cancellationToken = default);
}