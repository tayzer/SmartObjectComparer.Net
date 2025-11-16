// <copyright file="IFileUtilities.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Interface for file utilities.
/// </summary>
public interface IFileUtilities
{
    /// <summary>
    /// Creates a memory stream from a file stream.
    /// </summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    Task<MemoryStream> CreateMemoryStreamFromFileAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a report markdown file.
    /// </summary>
    /// <returns></returns>
    string GenerateReportMarkdown(DifferenceSummary summary, string additionalInfo = null);

    /// <summary>
    /// Generates a report for folder comparisons.
    /// </summary>
    /// <returns></returns>
    string GenerateFolderComparisonReport(MultiFolderComparisonResult folderResult);

    /// <summary>
    /// Generates a pattern analysis report.
    /// </summary>
    /// <returns></returns>
    string GeneratePatternAnalysisReport(ComparisonPatternAnalysis analysis);

    /// <summary>
    /// Generates a semantic difference analysis report.
    /// </summary>
    /// <param name="analysis">The semantic difference analysis.</param>
    /// <returns>Markdown content as a string.</returns>
    string GenerateSemanticAnalysisReport(SemanticDifferenceAnalysis analysis);
}
