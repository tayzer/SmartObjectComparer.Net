namespace ComparisonTool.Core.V2;

/// <summary>
/// Interface for file utilities
/// </summary>
public interface IFileUtils
{
    /// <summary>
    /// Creates a memory stream from a file stream
    /// </summary>
    Task<MemoryStream> CreateMemoryStreamFromFileAsync(Stream fileStream, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates a report markdown file
    /// </summary>
    string GenerateReportMarkdown(DifferenceSummary summary, string additionalInfo = null);

    /// <summary>
    /// Generates a report for folder comparisons
    /// </summary>
    string GenerateFolderComparisonReport(MultiFolderComparisonResult folderResult);

    /// <summary>
    /// Generates a pattern analysis report
    /// </summary>
    string GeneratePatternAnalysisReport(ComparisonPatternAnalysis analysis);
}