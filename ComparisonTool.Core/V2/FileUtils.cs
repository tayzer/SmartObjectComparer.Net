using System.Text;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.V2;

/// <summary>
/// Utility methods for file operations used in the comparison tool
/// </summary>
public class FileUtils : IFileUtils
{
    private readonly ILogger<FileUtils> _logger;

    public FileUtils(ILogger<FileUtils> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Creates a memory stream from a file stream
    /// </summary>
    /// <param name="fileStream">The source file stream</param>
    /// <param name="cancellationToken">Cancellation token for async operations</param>
    /// <returns>A memory stream containing the file contents</returns>
    public async Task<MemoryStream> CreateMemoryStreamFromFileAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        try
        {
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating memory stream from file");
            throw;
        }
    }

    /// <summary>
    /// Generates a report markdown file
    /// </summary>
    /// <param name="summary">The difference summary to generate report from</param>
    /// <param name="additionalInfo">Optional additional information to include at the top of the report</param>
    /// <returns>Markdown content as a string</returns>
    public string GenerateReportMarkdown(DifferenceSummary summary, string additionalInfo = null)
    {
        var sb = new StringBuilder();

        // Add additional information if provided
        if (!string.IsNullOrEmpty(additionalInfo))
        {
            sb.AppendLine(additionalInfo);
            sb.AppendLine();
        }
        else
        {
            sb.AppendLine("# Comparison Summary Report");
            sb.AppendLine();
        }

        if (summary.AreEqual)
        {
            sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
            return sb.ToString();
        }

        sb.AppendLine($"**Total Differences: {summary.TotalDifferenceCount}**");
        sb.AppendLine();

        // Summary by category
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in summary.DifferencesByChangeType.OrderByDescending(c => c.Value.Count))
        {
            sb.AppendLine($"| {FormatCategoryName(category.Key)} | {category.Value.Count} | {summary.CategoryPercentages[category.Key]}% |");
        }

        sb.AppendLine();

        // Summary by root object
        sb.AppendLine("## Differences by Root Object");
        sb.AppendLine();
        sb.AppendLine("| Object | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");

        foreach (var obj in summary.DifferencesByRootObject.OrderByDescending(o => o.Value.Count))
        {
            sb.AppendLine($"| {obj.Key} | {obj.Value.Count} | {summary.RootObjectPercentages[obj.Key]}% |");
        }

        sb.AppendLine();

        // Common patterns
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();

        foreach (var pattern in summary.CommonPatterns.Take(10)) // Top 10 patterns
        {
            sb.AppendLine($"### Pattern: {pattern.Pattern} ({pattern.OccurrenceCount} occurrences)");
            sb.AppendLine();
            sb.AppendLine("Example differences:");
            sb.AppendLine();

            foreach (var example in pattern.Examples)
            {
                sb.AppendLine($"- Property: `{example.PropertyName}`");
                sb.AppendLine($"  - Old: `{FormatValue(example.Object1Value)}`");
                sb.AppendLine($"  - New: `{FormatValue(example.Object2Value)}`");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a report for folder comparisons
    /// </summary>
    /// <param name="folderResult">The folder comparison result</param>
    /// <returns>Markdown content as a string</returns>
    public string GenerateFolderComparisonReport(MultiFolderComparisonResult folderResult)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# V1 vs V2 Folder Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| V1 File | V2 File | Status | Differences |");
        sb.AppendLine("|---------|---------|--------|------------|");

        foreach (var result in folderResult.FilePairResults)
        {
            var status = result.AreEqual ? "✓ Equal" : "❌ Different";
            var diffCount = result.AreEqual ? "0" : result.Summary.TotalDifferenceCount.ToString();

            sb.AppendLine($"| {result.File1Name} | {result.File2Name} | {status} | {diffCount} |");
        }

        sb.AppendLine();

        // Add detailed comparison for each file pair
        for (int i = 0; i < folderResult.FilePairResults.Count; i++)
        {
            var result = folderResult.FilePairResults[i];

            sb.AppendLine($"## Pair {i + 1}: {result.File1Name} vs {result.File2Name}");
            sb.AppendLine();

            if (result.AreEqual)
            {
                sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
            }
            else
            {
                // Include only the most important parts of the report for each file
                sb.AppendLine($"**Total Differences: {result.Summary.TotalDifferenceCount}**");
                sb.AppendLine();

                // Add top differences by category
                sb.AppendLine("### Differences by Category");
                sb.AppendLine();
                sb.AppendLine("| Category | Count | Percentage |");
                sb.AppendLine("|----------|-------|------------|");

                foreach (var category in result.Summary.DifferencesByChangeType.OrderByDescending(c => c.Value.Count).Take(5))
                {
                    sb.AppendLine($"| {FormatCategoryName(category.Key)} | {category.Value.Count} | {result.Summary.CategoryPercentages[category.Key]}% |");
                }

                sb.AppendLine();

                // Add a sample of actual differences
                sb.AppendLine("### Sample Differences");
                sb.AppendLine();

                var sampleDiffs = result.Result.Differences.Take(10).ToList();
                foreach (var diff in sampleDiffs)
                {
                    sb.AppendLine($"- Property: `{diff.PropertyName}`");
                    sb.AppendLine($"  - V1: `{FormatValue(diff.Object1Value)}`");
                    sb.AppendLine($"  - V2: `{FormatValue(diff.Object2Value)}`");
                    sb.AppendLine();
                }

                if (result.Result.Differences.Count > 10)
                {
                    sb.AppendLine($"*...and {result.Result.Differences.Count - 10} more differences*");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a pattern analysis report
    /// </summary>
    /// <param name="analysis">The pattern analysis data</param>
    /// <returns>Markdown content as a string</returns>
    public string GeneratePatternAnalysisReport(ComparisonPatternAnalysis analysis)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# XML Comparison Pattern Analysis");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine($"- **Total Files Compared:** {analysis.TotalFilesPaired} pairs");
        sb.AppendLine($"- **Files With Differences:** {analysis.FilesWithDifferences} pairs");
        sb.AppendLine($"- **Total Differences Found:** {analysis.TotalDifferences}");
        sb.AppendLine();

        // Category breakdown
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in analysis.TotalByCategory.OrderByDescending(c => c.Value))
        {
            if (category.Value > 0)
            {
                double percentage = (double)category.Value / analysis.TotalDifferences * 100;
                sb.AppendLine($"| {FormatCategoryName(category.Key)} | {category.Value} | {percentage:F1}% |");
            }
        }
        sb.AppendLine();

        // Common patterns across files
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();
        sb.AppendLine("The following property paths showed differences in multiple files:");
        sb.AppendLine();
        sb.AppendLine("| Property Path | Files Affected | Total Occurrences |");
        sb.AppendLine("|--------------|----------------|-------------------|");

        foreach (var pattern in analysis.CommonPathPatterns)
        {
            sb.AppendLine($"| `{pattern.PatternPath}` | {pattern.FileCount} | {pattern.OccurrenceCount} |");
        }
        sb.AppendLine();

        // Common value changes
        sb.AppendLine("## Common Property Value Changes");
        sb.AppendLine();
        sb.AppendLine("The following specific property value changes appeared in multiple files:");
        sb.AppendLine();

        foreach (var change in analysis.CommonPropertyChanges.Take(10))
        {
            sb.AppendLine($"### `{change.PropertyName}`");
            sb.AppendLine();
            sb.AppendLine($"Changed in {change.AffectedFiles.Count} files, {change.OccurrenceCount} total occurrences");
            sb.AppendLine();

            foreach (var valueChange in change.CommonChanges)
            {
                sb.AppendLine($"- From: `{TruncateText(valueChange.Key, 50)}` → To: `{TruncateText(valueChange.Value, 50)}`");
            }
            sb.AppendLine();
            sb.AppendLine("Affected files:");
            sb.AppendLine();
            foreach (var file in change.AffectedFiles.Take(5))
            {
                sb.AppendLine($"- {file}");
            }

            if (change.AffectedFiles.Count > 5)
            {
                sb.AppendLine($"- ...and {change.AffectedFiles.Count - 5} more");
            }

            sb.AppendLine();
        }

        // File groups
        if (analysis.SimilarFileGroups.Count > 0)
        {
            sb.AppendLine("## File Similarity Groups");
            sb.AppendLine();
            sb.AppendLine("Files with similar difference patterns have been grouped:");
            sb.AppendLine();

            foreach (var group in analysis.SimilarFileGroups.OrderByDescending(g => g.FileCount))
            {
                sb.AppendLine($"### {group.GroupName} ({group.FileCount} files)");
                sb.AppendLine();
                sb.AppendLine($"**Pattern:** {group.CommonPattern}");
                sb.AppendLine();
                sb.AppendLine("Files in this group:");
                sb.AppendLine();

                foreach (var file in group.FilePairs)
                {
                    sb.AppendLine($"- {file}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private string FormatCategoryName(DifferenceCategory category)
    {
        return category switch
        {
            DifferenceCategory.TextContentChanged => "Text Content Changed",
            DifferenceCategory.NumericValueChanged => "Numeric Value Changed",
            DifferenceCategory.DateTimeChanged => "Date/Time Changed",
            DifferenceCategory.BooleanValueChanged => "Boolean Value Changed",
            DifferenceCategory.CollectionItemChanged => "Collection Item Changed",
            DifferenceCategory.ItemAdded => "Item Added",
            DifferenceCategory.ItemRemoved => "Item Removed",
            DifferenceCategory.NullValueChange => "Null Value Change",
            _ => "Other"
        };
    }

    private string FormatValue(object value, int maxLength = 100)
    {
        if (value == null)
            return "null";

        if (value is DateTime dt)
            return dt.ToString("yyyy-MM-dd HH:mm:ss");

        if (value is string str)
        {
            if (str.Length <= maxLength)
                return str;

            return str.Substring(0, maxLength - 3) + "...";
        }

        return value.ToString();
    }

    private string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength - 3) + "...";
    }
}