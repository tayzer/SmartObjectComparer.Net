using System.Globalization;
using System.Linq;
using System.Text;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility methods for file operations used in the comparison tool.
/// </summary>
public class FileUtilities : IFileUtilities
{
    private readonly ILogger<FileUtilities> logger;

    public FileUtilities(ILogger<FileUtilities> logger) => this.logger = logger;

    /// <summary>
    /// Creates a memory stream from a file stream.
    /// </summary>
    /// <param name="fileStream">The source file stream.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A memory stream containing the file contents.</returns>
    public async Task<MemoryStream> CreateMemoryStreamFromFileAsync(Stream fileStream, CancellationToken cancellationToken = default)
    {
        try
        {
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken).ConfigureAwait(false);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating memory stream from file");
            throw;
        }
    }

    /// <summary>
    /// Generates a report markdown file.
    /// </summary>
    /// <param name="summary">The difference summary to generate report from.</param>
    /// <param name="additionalInfo">Optional additional information to include at the top of the report.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateReportMarkdown(DifferenceSummary summary, string? additionalInfo = null)
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

        AppendLineInvariant(sb, "**Total Differences: {0}**", summary.TotalDifferenceCount);
        sb.AppendLine();

        AppendDifferencesByCategory(sb, summary);
        AppendDifferencesByRootObject(sb, summary);
        AppendCommonPatterns(sb, summary);

        return sb.ToString();
    }

    /// <summary>
    /// Generates a report for folder comparisons.
    /// </summary>
    /// <param name="folderResult">The folder comparison result.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateFolderComparisonReport(MultiFolderComparisonResult folderResult)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Expected vs Actual Folder Comparison Report");
        sb.AppendLine();
        AppendLineInvariant(sb, "Date: {0:yyyy-MM-dd HH:mm:ss}", DateTime.Now);
        sb.AppendLine();

        AppendFolderComparisonSummary(sb, folderResult);
        AppendFolderComparisonDetails(sb, folderResult);

        return sb.ToString();
    }

    /// <summary>
    /// Generates a pattern analysis report.
    /// </summary>
    /// <param name="analysis">The pattern analysis data.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GeneratePatternAnalysisReport(ComparisonPatternAnalysis analysis)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# XML Comparison Pattern Analysis");
        sb.AppendLine();
        sb.AppendLine("## Summary");
        sb.AppendLine();
        AppendLineInvariant(sb, "- **Total Files Compared:** {0} pairs", analysis.TotalFilesPaired);
        AppendLineInvariant(sb, "- **Files With Differences:** {0} pairs", analysis.FilesWithDifferences);
        AppendLineInvariant(sb, "- **Total Differences Found:** {0}", analysis.TotalDifferences);
        sb.AppendLine();

        AppendPatternCategoryBreakdown(sb, analysis);
        AppendCommonPathPatterns(sb, analysis);
        AppendCommonPropertyChanges(sb, analysis);
        AppendSimilarFileGroups(sb, analysis);

        return sb.ToString();
    }

    /// <summary>
    /// Generates a semantic difference analysis report.
    /// </summary>
    /// <param name="analysis">The semantic difference analysis.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateSemanticAnalysisReport(SemanticDifferenceAnalysis analysis)
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Semantic Difference Analysis Report");
        sb.AppendLine();
        sb.AppendLine("## Overview");
        sb.AppendLine();

        AppendSemanticOverview(sb, analysis);
        AppendSemanticGroupsTable(sb, analysis);
        AppendSemanticGroupDetails(sb, analysis);

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("This report highlights semantically grouped differences to help focus testing efforts. Pay special attention to:");
        sb.AppendLine();

        AppendSemanticSummary(sb, analysis);

        return sb.ToString();
    }

    private static void AppendDifferencesByRootObject(StringBuilder sb, DifferenceSummary summary)
    {
        sb.AppendLine("## Differences by Root Object");
        sb.AppendLine();
        sb.AppendLine("| Object | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");

        foreach (var obj in summary.DifferencesByRootObject.OrderByDescending(o => o.Value.Count))
        {
            AppendLineInvariant(
                sb,
                "| {0} | {1} | {2}% |",
                obj.Key,
                obj.Value.Count,
                summary.RootObjectPercentages[obj.Key]);
        }

        sb.AppendLine();
    }

    private static void AppendCommonPatterns(StringBuilder sb, DifferenceSummary summary)
    {
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();

        // Top 10 patterns
        foreach (var pattern in summary.CommonPatterns.Take(10))
        {
            AppendLineInvariant(sb, "### Pattern: {0} ({1} occurrences)", pattern.Pattern, pattern.OccurrenceCount);
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
    }

    private static void AppendFolderComparisonSummary(StringBuilder sb, MultiFolderComparisonResult folderResult)
    {
        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Expected File | Actual File | Status | Differences |");
        sb.AppendLine("|---------|---------|--------|------------|");

        foreach (var result in folderResult.FilePairResults)
        {
            var status = result.AreEqual ? "✓ Equal" : "❌ Different";
            var diffCount = result.AreEqual
                ? "0"
                : (result.Summary?.TotalDifferenceCount ?? 0).ToString(CultureInfo.InvariantCulture);

            sb.AppendLine($"| {result.File1Name} | {result.File2Name} | {status} | {diffCount} |");
        }

        sb.AppendLine();
    }

    private static void AppendCommonPathPatterns(StringBuilder sb, ComparisonPatternAnalysis analysis)
    {
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();
        sb.AppendLine("The following property paths showed differences in multiple files:");
        sb.AppendLine();
        sb.AppendLine("| Property Path | Files Affected | Total Occurrences |");
        sb.AppendLine("|--------------|----------------|-------------------|");

        foreach (var pattern in analysis.CommonPathPatterns)
        {
            AppendLineInvariant(
                sb,
                "| `{0}` | {1} | {2} |",
                pattern.PatternPath,
                pattern.FileCount,
                pattern.OccurrenceCount);
        }

        sb.AppendLine();
    }

    private static void AppendCommonPropertyChanges(StringBuilder sb, ComparisonPatternAnalysis analysis)
    {
        sb.AppendLine("## Common Property Value Changes");
        sb.AppendLine();
        sb.AppendLine("The following specific property value changes appeared in multiple files:");
        sb.AppendLine();

        foreach (var change in analysis.CommonPropertyChanges.Take(10))
        {
            sb.AppendLine($"### `{change.PropertyName}`");
            sb.AppendLine();
            AppendLineInvariant(
                sb,
                "Changed in {0} files, {1} total occurrences",
                change.AffectedFiles.Count,
                change.OccurrenceCount);
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
                AppendLineInvariant(sb, "- ...and {0} more", change.AffectedFiles.Count - 5);
            }

            sb.AppendLine();
        }
    }

    private static void AppendSimilarFileGroups(StringBuilder sb, ComparisonPatternAnalysis analysis)
    {
        if (analysis.SimilarFileGroups.Count <= 0)
        {
            return;
        }

        sb.AppendLine("## File Similarity Groups");
        sb.AppendLine();
        sb.AppendLine("Files with similar difference patterns have been grouped:");
        sb.AppendLine();

        foreach (var group in analysis.SimilarFileGroups.OrderByDescending(g => g.FileCount))
        {
            AppendLineInvariant(sb, "### {0} ({1} files)", group.GroupName, group.FileCount);
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

    private static void AppendSemanticOverview(StringBuilder sb, SemanticDifferenceAnalysis analysis)
    {
        AppendLineInvariant(sb, "- **Total Differences Analyzed:** {0}", analysis.TotalDifferences);
        AppendLineInvariant(
            sb,
            "- **Differences Semantically Categorized:** {0} ({1:F1}%)",
            analysis.CategorizedDifferences,
            analysis.CategorizedPercentage);
        AppendLineInvariant(sb, "- **Number of Semantic Groups:** {0}", analysis.SemanticGroups.Count);
        sb.AppendLine();
    }

    private static void AppendSemanticGroupsTable(StringBuilder sb, SemanticDifferenceAnalysis analysis)
    {
        sb.AppendLine("## Semantic Groups");
        sb.AppendLine();
        sb.AppendLine("| Group | Description | Differences | Files | Confidence |");
        sb.AppendLine("|-------|-------------|-------------|-------|------------|");

        foreach (var group in analysis.SemanticGroups)
        {
            AppendLineInvariant(
                sb,
                "| **{0}** | {1} | {2} | {3} | {4}% |",
                group.GroupName,
                group.SemanticDescription,
                group.DifferenceCount,
                group.FileCount,
                group.ConfidenceLevel);
        }

        sb.AppendLine();
    }

    private static void AppendTestingRecommendations(StringBuilder sb, SemanticDifferenceGroup group)
    {
        sb.AppendLine("#### Testing Recommendations");
        sb.AppendLine();
        sb.AppendLine($"When testing these changes, focus on validating that {group.GroupName.ToLowerInvariant()} are correctly handled throughout the application.");
        sb.AppendLine("Pay special attention to:");
        sb.AppendLine();

        switch (group.GroupName)
        {
            case "Status Changes":
                sb.AppendLine("- Verify the status transitions are valid according to business rules");
                sb.AppendLine("- Check that UI correctly reflects different statuses with appropriate styling");
                sb.AppendLine("- Confirm status-dependent behaviors work correctly");
                break;

            case "ID Value Changes":
                sb.AppendLine("- Ensure consistent ID usage across related entities");
                sb.AppendLine("- Verify reference integrity - check that the new IDs are used consistently");
                sb.AppendLine("- Test lookup operations using the new ID values");
                break;

            case "Timestamp/Date Changes":
                sb.AppendLine("- Verify date calculations and comparisons");
                sb.AppendLine("- Check date formatting in different contexts");
                sb.AppendLine("- Test date-sensitive business logic");
                break;

            case "Score/Value Adjustments":
                sb.AppendLine("- Verify calculations that depend on these values");
                sb.AppendLine("- Validate thresholds and boundaries still function correctly");
                sb.AppendLine("- Check that UI elements properly represent the new values");
                break;

            case "Name/Description Changes":
                sb.AppendLine("- Check for text truncation in UI components");
                sb.AppendLine("- Verify translated content if localization is supported");
                sb.AppendLine("- Test search/filter functionality with the new text values");
                break;

            case "Collection Order Changes":
                sb.AppendLine("- Verify sort operations behave correctly");
                sb.AppendLine("- Check pagination if applicable");
                sb.AppendLine("- Test operations that rely on specific positions within collections");
                break;

            case "Tag Modifications":
                sb.AppendLine("- Test filtering and categorization features");
                sb.AppendLine("- Verify tag-based reporting");
                sb.AppendLine("- Check for visual indication of tags in the UI");
                break;

            default:
                sb.AppendLine("- Test the complete end-to-end flow involving these properties");
                sb.AppendLine("- Check data validation rules for the affected fields");
                sb.AppendLine("- Verify related calculations and business logic");
                break;
        }
    }

    private static void AppendSemanticSummary(StringBuilder sb, SemanticDifferenceAnalysis analysis)
    {
        var topGroups = analysis.SemanticGroups
            .OrderByDescending(g => g.ConfidenceLevel * g.DifferenceCount)
            .Take(3);

        foreach (var group in topGroups)
        {
            AppendLineInvariant(
                sb,
                "- **{0}** - {1} differences across {2} files",
                group.GroupName,
                group.DifferenceCount,
                group.FileCount);
        }
    }

    private static void AppendLineInvariant(StringBuilder sb, string format, params object[] args) => sb.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));

    private static string FormatCategoryName(DifferenceCategory category) =>
        category switch
        {
            DifferenceCategory.NumericValueChanged => "Numeric Value Changed",
            DifferenceCategory.DateTimeChanged => "Date/Time Changed",
            DifferenceCategory.BooleanValueChanged => "Boolean Value Changed",
            DifferenceCategory.CollectionItemChanged => "Collection Item Changed",
            DifferenceCategory.ItemAdded => "Item Added",
            DifferenceCategory.ItemRemoved => "Item Removed",
            DifferenceCategory.NullValueChange => "Null Value Change",
            DifferenceCategory.ValueChanged => "Value Changed",
            _ => "Other"
        };

    private static string FormatValue(object value, int maxLength = 100)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (value is string str)
        {
            if (str.Length <= maxLength)
            {
                return str;
            }

            return str.Substring(0, maxLength - 3) + "...";
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }

    private static string TruncateText(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text.Substring(0, maxLength - 3) + "...";
    }

    private void AppendDifferencesByCategory(StringBuilder sb, DifferenceSummary summary)
    {
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in summary.DifferencesByChangeType.OrderByDescending(c => c.Value.Count))
        {
            AppendLineInvariant(
                sb,
                "| {0} | {1} | {2}% |",
                FormatCategoryName(category.Key),
                category.Value.Count,
                summary.CategoryPercentages[category.Key]);
        }

        sb.AppendLine();
    }

    private void AppendFolderComparisonDetails(StringBuilder sb, MultiFolderComparisonResult folderResult)
    {
        for (var i = 0; i < folderResult.FilePairResults.Count; i++)
        {
            AppendFolderComparisonPairDetails(sb, i + 1, folderResult.FilePairResults[i]);
        }
    }

    private void AppendFolderComparisonPairDetails(StringBuilder sb, int pairNumber, FilePairComparisonResult result)
    {
        AppendLineInvariant(sb, "## Pair {0}: {1} vs {2}", pairNumber, result.File1Name, result.File2Name);
        sb.AppendLine();

        if (result.AreEqual)
        {
            sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
        }
        else
        {
            AppendLineInvariant(sb, "**Total Differences: {0}**", result.Summary.TotalDifferenceCount);
            sb.AppendLine();

            sb.AppendLine("### Differences by Category");
            sb.AppendLine();
            sb.AppendLine("| Category | Count | Percentage |");
            sb.AppendLine("|----------|-------|------------|");

            foreach (var category in (result.Summary?.DifferencesByChangeType ?? new Dictionary<DifferenceCategory, List<Difference>>()).OrderByDescending(c => c.Value.Count).Take(5))
            {
                if (result.Summary != null && result.Summary.CategoryPercentages != null && result.Summary.CategoryPercentages.TryGetValue(category.Key, out var pct))
                {
                    AppendLineInvariant(
                        sb,
                        "| {0} | {1} | {2}% |",
                        FormatCategoryName(category.Key),
                        category.Value.Count,
                        pct);
                }
                else
                {
                    AppendLineInvariant(
                        sb,
                        "| {0} | {1} | 0% |",
                        FormatCategoryName(category.Key),
                        category.Value.Count);
                }
            }

            sb.AppendLine();

            sb.AppendLine("### Sample Differences");
            sb.AppendLine();

            var sampleDiffs = result.Result?.Differences?.Take(10).ToList() ?? new System.Collections.Generic.List<KellermanSoftware.CompareNetObjects.Difference>();
            foreach (var diff in sampleDiffs)
            {
                sb.AppendLine($"- Property: `{diff.PropertyName}`");
                sb.AppendLine($"  - Expected: `{FormatValue(diff.Object1Value)}`");
                sb.AppendLine($"  - Actual: `{FormatValue(diff.Object2Value)}`");
                sb.AppendLine();
            }

            var totalDiffCount = result.Result?.Differences?.Count ?? 0;
            if (totalDiffCount > 10)
            {
                AppendLineInvariant(sb, "*...and {0} more differences*", Math.Max(0, totalDiffCount - 10));
                sb.AppendLine();
            }
        }

        sb.AppendLine("---");
        sb.AppendLine();
    }

    private void AppendPatternCategoryBreakdown(StringBuilder sb, ComparisonPatternAnalysis analysis)
    {
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in analysis.TotalByCategory.OrderByDescending(c => c.Value))
        {
            if (category.Value > 0)
            {
                var percentage = (double)category.Value / analysis.TotalDifferences * 100;
                AppendLineInvariant(
                    sb,
                    "| {0} | {1} | {2:F1}% |",
                    FormatCategoryName(category.Key),
                    category.Value,
                    percentage);
            }
        }

        sb.AppendLine();
    }

    private void AppendSemanticGroupDetails(StringBuilder sb, SemanticDifferenceAnalysis analysis)
    {
        foreach (var group in analysis.SemanticGroups)
        {
            sb.AppendLine($"### {group.GroupName}");
            sb.AppendLine();
            sb.AppendLine($"**Description:** {group.SemanticDescription}");
            AppendLineInvariant(sb, "**Confidence:** {0}%", group.ConfidenceLevel);
            AppendLineInvariant(sb, "**Differences:** {0}", group.DifferenceCount);
            AppendLineInvariant(sb, "**Files Affected:** {0}", group.FileCount);
            sb.AppendLine();

            sb.AppendLine("#### Related Properties");
            sb.AppendLine();
            foreach (var prop in group.RelatedProperties.Take(10))
            {
                sb.AppendLine($"- `{prop}`");
            }

            if (group.RelatedProperties.Count > 10)
            {
                AppendLineInvariant(sb, "- *...and {0} more properties*", group.RelatedProperties.Count - 10);
            }

            sb.AppendLine();
            sb.AppendLine("#### Example Differences");
            sb.AppendLine();
            sb.AppendLine("| Property | Old Value | New Value |");
            sb.AppendLine("|----------|-----------|-----------|");

            foreach (var diff in group.Differences.Take(5))
            {
                sb.AppendLine($"| `{diff.PropertyName}` | {TruncateText(FormatValue(diff.Object1Value), 50)} | {TruncateText(FormatValue(diff.Object2Value), 50)} |");
            }

            sb.AppendLine();

            if (group.Differences.Count > 5)
            {
                AppendLineInvariant(sb, "*...and {0} more differences*", group.Differences.Count - 5);
                sb.AppendLine();
            }

            AppendTestingRecommendations(sb, group);

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }
    }
}
