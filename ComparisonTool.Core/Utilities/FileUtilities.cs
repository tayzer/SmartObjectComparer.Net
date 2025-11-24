// <copyright file="FileUtilities.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using System.Linq;
using KellermanSoftware.CompareNetObjects;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Utilities;

/// <summary>
/// Utility methods for file operations used in the comparison tool.
/// </summary>
public class FileUtilities : IFileUtilities {
    private readonly ILogger<FileUtilities> logger;

    public FileUtilities(ILogger<FileUtilities> logger) {
        this.logger = logger;
    }

    /// <summary>
    /// Creates a memory stream from a file stream.
    /// </summary>
    /// <param name="fileStream">The source file stream.</param>
    /// <param name="cancellationToken">Cancellation token for async operations.</param>
    /// <returns>A memory stream containing the file contents.</returns>
    public async Task<MemoryStream> CreateMemoryStreamFromFileAsync(Stream fileStream, CancellationToken cancellationToken = default) {
        try {
            var memoryStream = new MemoryStream();
            await fileStream.CopyToAsync(memoryStream, cancellationToken);
            memoryStream.Position = 0;
            return memoryStream;
        }
        catch (Exception ex) {
            this.logger.LogError(ex, "Error creating memory stream from file");
            throw;
        }
    }

    /// <summary>
    /// Generates a report markdown file.
    /// </summary>
    /// <param name="summary">The difference summary to generate report from.</param>
    /// <param name="additionalInfo">Optional additional information to include at the top of the report.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateReportMarkdown(DifferenceSummary summary, string additionalInfo = null) {
        var sb = new StringBuilder();

        // Add additional information if provided
        if (!string.IsNullOrEmpty(additionalInfo)) {
            sb.AppendLine(additionalInfo);
            sb.AppendLine();
        }
        else {
            sb.AppendLine("# Comparison Summary Report");
            sb.AppendLine();
        }

        if (summary.AreEqual) {
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

        foreach (var category in summary.DifferencesByChangeType.OrderByDescending(c => c.Value.Count)) {
            sb.AppendLine($"| {this.FormatCategoryName(category.Key)} | {category.Value.Count} | {summary.CategoryPercentages[category.Key]}% |");
        }

        sb.AppendLine();

        // Summary by root object
        sb.AppendLine("## Differences by Root Object");
        sb.AppendLine();
        sb.AppendLine("| Object | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");

        foreach (var obj in summary.DifferencesByRootObject.OrderByDescending(o => o.Value.Count)) {
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

            foreach (var example in pattern.Examples) {
                sb.AppendLine($"- Property: `{example.PropertyName}`");
                sb.AppendLine($"  - Old: `{this.FormatValue(example.Object1Value)}`");
                sb.AppendLine($"  - New: `{this.FormatValue(example.Object2Value)}`");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a report for folder comparisons.
    /// </summary>
    /// <param name="folderResult">The folder comparison result.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateFolderComparisonReport(MultiFolderComparisonResult folderResult) {
        var sb = new StringBuilder();

        sb.AppendLine("# Expected vs Actual Folder Comparison Report");
        sb.AppendLine();
        sb.AppendLine($"Date: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine();

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("| Expected File | Actual File | Status | Differences |");
        sb.AppendLine("|---------|---------|--------|------------|");

        foreach (var result in folderResult.FilePairResults) {
            var status = result.AreEqual ? "✓ Equal" : "❌ Different";
            var diffCount = result.AreEqual ? "0" : (result.Summary?.TotalDifferenceCount ?? 0).ToString();

            sb.AppendLine($"| {result.File1Name} | {result.File2Name} | {status} | {diffCount} |");
        }

        sb.AppendLine();

        // Add detailed comparison for each file pair
        for (var i = 0; i < folderResult.FilePairResults.Count; i++) {
            var result = folderResult.FilePairResults[i];

            sb.AppendLine($"## Pair {i + 1}: {result.File1Name} vs {result.File2Name}");
            sb.AppendLine();

            if (result.AreEqual) {
                sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
            }
            else {
                // Include only the most important parts of the report for each file
                sb.AppendLine($"**Total Differences: {result.Summary.TotalDifferenceCount}**");
                sb.AppendLine();

                // Add top differences by category
                sb.AppendLine("### Differences by Category");
                sb.AppendLine();
                sb.AppendLine("| Category | Count | Percentage |");
                sb.AppendLine("|----------|-------|------------|");

                foreach (var category in (result.Summary?.DifferencesByChangeType ?? new System.Collections.Generic.Dictionary<DifferenceCategory, System.Collections.Generic.List<Difference>>()).OrderByDescending(c => c.Value.Count).Take(5)) {
                    if (result.Summary != null && result.Summary.CategoryPercentages != null && result.Summary.CategoryPercentages.TryGetValue(category.Key, out var pct)) {
                        sb.AppendLine($"| {this.FormatCategoryName(category.Key)} | {category.Value.Count} | {pct}% |");
                    }
                    else {
                        sb.AppendLine($"| {this.FormatCategoryName(category.Key)} | {category.Value.Count} | 0% |");
                    }
                }

                sb.AppendLine();

                // Add a sample of actual differences
                sb.AppendLine("### Sample Differences");
                sb.AppendLine();

                var sampleDiffs = result.Result?.Differences?.Take(10).ToList() ?? new System.Collections.Generic.List<KellermanSoftware.CompareNetObjects.Difference>();
                foreach (var diff in sampleDiffs) {
                    sb.AppendLine($"- Property: `{diff.PropertyName}`");
                    sb.AppendLine($"  - Expected: `{this.FormatValue(diff.Object1Value)}`");
                    sb.AppendLine($"  - Actual: `{this.FormatValue(diff.Object2Value)}`");
                    sb.AppendLine();
                }
                var totalDiffCount = result.Result?.Differences?.Count ?? 0;
                if (totalDiffCount > 10) {
                    sb.AppendLine($"*...and {Math.Max(0, totalDiffCount - 10)} more differences*");
                    sb.AppendLine();
                }
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a pattern analysis report.
    /// </summary>
    /// <param name="analysis">The pattern analysis data.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GeneratePatternAnalysisReport(ComparisonPatternAnalysis analysis) {
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

        foreach (var category in analysis.TotalByCategory.OrderByDescending(c => c.Value)) {
            if (category.Value > 0) {
                var percentage = (double)category.Value / analysis.TotalDifferences * 100;
                sb.AppendLine($"| {this.FormatCategoryName(category.Key)} | {category.Value} | {percentage:F1}% |");
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

        foreach (var pattern in analysis.CommonPathPatterns) {
            sb.AppendLine($"| `{pattern.PatternPath}` | {pattern.FileCount} | {pattern.OccurrenceCount} |");
        }

        sb.AppendLine();

        // Common value changes
        sb.AppendLine("## Common Property Value Changes");
        sb.AppendLine();
        sb.AppendLine("The following specific property value changes appeared in multiple files:");
        sb.AppendLine();

        foreach (var change in analysis.CommonPropertyChanges.Take(10)) {
            sb.AppendLine($"### `{change.PropertyName}`");
            sb.AppendLine();
            sb.AppendLine($"Changed in {change.AffectedFiles.Count} files, {change.OccurrenceCount} total occurrences");
            sb.AppendLine();

            foreach (var valueChange in change.CommonChanges) {
                sb.AppendLine($"- From: `{this.TruncateText(valueChange.Key, 50)}` → To: `{this.TruncateText(valueChange.Value, 50)}`");
            }

            sb.AppendLine();
            sb.AppendLine("Affected files:");
            sb.AppendLine();
            foreach (var file in change.AffectedFiles.Take(5)) {
                sb.AppendLine($"- {file}");
            }

            if (change.AffectedFiles.Count > 5) {
                sb.AppendLine($"- ...and {change.AffectedFiles.Count - 5} more");
            }

            sb.AppendLine();
        }

        // File groups
        if (analysis.SimilarFileGroups.Count > 0) {
            sb.AppendLine("## File Similarity Groups");
            sb.AppendLine();
            sb.AppendLine("Files with similar difference patterns have been grouped:");
            sb.AppendLine();

            foreach (var group in analysis.SimilarFileGroups.OrderByDescending(g => g.FileCount)) {
                sb.AppendLine($"### {group.GroupName} ({group.FileCount} files)");
                sb.AppendLine();
                sb.AppendLine($"**Pattern:** {group.CommonPattern}");
                sb.AppendLine();
                sb.AppendLine("Files in this group:");
                sb.AppendLine();

                foreach (var file in group.FilePairs) {
                    sb.AppendLine($"- {file}");
                }

                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Generates a semantic difference analysis report.
    /// </summary>
    /// <param name="analysis">The semantic difference analysis.</param>
    /// <returns>Markdown content as a string.</returns>
    public string GenerateSemanticAnalysisReport(SemanticDifferenceAnalysis analysis) {
        var sb = new StringBuilder();

        sb.AppendLine("# Semantic Difference Analysis Report");
        sb.AppendLine();
        sb.AppendLine("## Overview");
        sb.AppendLine();
        sb.AppendLine($"- **Total Differences Analyzed:** {analysis.TotalDifferences}");
        sb.AppendLine($"- **Differences Semantically Categorized:** {analysis.CategorizedDifferences} ({analysis.CategorizedPercentage:F1}%)");
        sb.AppendLine($"- **Number of Semantic Groups:** {analysis.SemanticGroups.Count}");
        sb.AppendLine();

        sb.AppendLine("## Semantic Groups");
        sb.AppendLine();
        sb.AppendLine("| Group | Description | Differences | Files | Confidence |");
        sb.AppendLine("|-------|-------------|-------------|-------|------------|");

        foreach (var group in analysis.SemanticGroups) {
            sb.AppendLine($"| **{group.GroupName}** | {group.SemanticDescription} | {group.DifferenceCount} | {group.FileCount} | {group.ConfidenceLevel}% |");
        }

        sb.AppendLine();

        foreach (var group in analysis.SemanticGroups) {
            sb.AppendLine($"### {group.GroupName}");
            sb.AppendLine();
            sb.AppendLine($"**Description:** {group.SemanticDescription}");
            sb.AppendLine($"**Confidence:** {group.ConfidenceLevel}%");
            sb.AppendLine($"**Differences:** {group.DifferenceCount}");
            sb.AppendLine($"**Files Affected:** {group.FileCount}");
            sb.AppendLine();

            // Related properties
            sb.AppendLine("#### Related Properties");
            sb.AppendLine();
            foreach (var prop in group.RelatedProperties.Take(10)) {
                sb.AppendLine($"- `{prop}`");
            }

            if (group.RelatedProperties.Count > 10) {
                sb.AppendLine($"- *...and {group.RelatedProperties.Count - 10} more properties*");
            }

            sb.AppendLine();

            // Example differences
            sb.AppendLine("#### Example Differences");
            sb.AppendLine();
            sb.AppendLine("| Property | Old Value | New Value |");
            sb.AppendLine("|----------|-----------|-----------|");

            foreach (var diff in group.Differences.Take(5)) {
                sb.AppendLine($"| `{diff.PropertyName}` | {this.TruncateText(this.FormatValue(diff.Object1Value), 50)} | {this.TruncateText(this.FormatValue(diff.Object2Value), 50)} |");
            }

            sb.AppendLine();

            if (group.Differences.Count > 5) {
                sb.AppendLine($"*...and {group.Differences.Count - 5} more differences*");
                sb.AppendLine();
            }

            // Testing guidance
            sb.AppendLine("#### Testing Recommendations");
            sb.AppendLine();
            sb.AppendLine($"When testing these changes, focus on validating that {group.GroupName.ToLower()} are correctly handled throughout the application.");
            sb.AppendLine("Pay special attention to:");
            sb.AppendLine();

            // Generate specific testing recommendations based on the group
            switch (group.GroupName) {
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

            sb.AppendLine();
            sb.AppendLine("---");
            sb.AppendLine();
        }

        sb.AppendLine("## Summary");
        sb.AppendLine();
        sb.AppendLine("This report highlights semantically grouped differences to help focus testing efforts. Pay special attention to:");
        sb.AppendLine();

        // Highlight the top 3 groups by confidence and size
        var topGroups = analysis.SemanticGroups
            .OrderByDescending(g => g.ConfidenceLevel * g.DifferenceCount)
            .Take(3);

        foreach (var group in topGroups) {
            sb.AppendLine($"- **{group.GroupName}** - {group.DifferenceCount} differences across {group.FileCount} files");
        }

        return sb.ToString();
    }

    private string FormatCategoryName(DifferenceCategory category) {
        return category switch {
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
    }

    private string FormatValue(object value, int maxLength = 100) {
        if (value == null) {
            return "null";
        }

        if (value is DateTime dt) {
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (value is string str) {
            if (str.Length <= maxLength) {
                return str;
            }

            return str.Substring(0, maxLength - 3) + "...";
        }

        return value.ToString();
    }

    private string TruncateText(string text, int maxLength) {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLength) {
            return text;
        }

        return text.Substring(0, maxLength - 3) + "...";
    }
}
