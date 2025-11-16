// <copyright file="DifferenceSummary.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Text;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Summary of the comparison differences.
/// </summary>
public class DifferenceSummary
{
    public bool AreEqual { get; set; }

    public int TotalDifferenceCount { get; set; }

    public Dictionary<DifferenceCategory, List<Difference>> DifferencesByChangeType { get; set; } = new ();

    public Dictionary<string, List<Difference>> DifferencesByRootObject { get; set; } = new ();

    public Dictionary<string, Dictionary<DifferenceCategory, List<Difference>>> DifferencesByRootObjectAndCategory { get; set; } = new ();

    public Dictionary<DifferenceCategory, double> CategoryPercentages { get; set; } = new ();

    public Dictionary<string, double> RootObjectPercentages { get; set; } = new ();

    public List<DifferencePattern> CommonPatterns { get; set; } = new ();

    /// <summary>
    /// Generate a human-friendly summary report.
    /// </summary>
    /// <returns></returns>
    public string GenerateReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Comparison Summary Report");
        sb.AppendLine();

        if (this.AreEqual)
        {
            sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
            return sb.ToString();
        }

        sb.AppendLine($"**Total Differences: {this.TotalDifferenceCount}**");
        sb.AppendLine();

        // Summary by category
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in this.DifferencesByChangeType.OrderByDescending(c => c.Value.Count))
        {
            sb.AppendLine($"| {this.FormatCategoryName(category.Key)} | {category.Value.Count} | {this.CategoryPercentages[category.Key]}% |");
        }

        sb.AppendLine();

        // Summary by root object and category
        sb.AppendLine("## Differences by Root Object and Category");
        sb.AppendLine();
        foreach (var obj in this.DifferencesByRootObjectAndCategory.OrderByDescending(o => o.Value.SelectMany(v => v.Value).Count()))
        {
            var total = obj.Value.SelectMany(v => v.Value).Count();
            sb.AppendLine($"### {obj.Key} (Total: {total})");
            foreach (var cat in obj.Value.OrderByDescending(c => c.Value.Count))
            {
                sb.AppendLine($"- {this.FormatCategoryName(cat.Key)}: {cat.Value.Count}");
                foreach (var diff in cat.Value.Take(5)) // show up to 5 examples
                {
                    sb.AppendLine($"    - Property: `{diff.PropertyName}` | Old: `{this.FormatValue(diff.Object1Value)}` | New: `{this.FormatValue(diff.Object2Value)}`");
                }

                if (cat.Value.Count > 5) {
                    sb.AppendLine($"    ...and {cat.Value.Count - 5} more");
                }
            }

            sb.AppendLine();
        }

        // Fallback: Summary by root object (legacy)
        sb.AppendLine("## Differences by Root Object (Legacy)");
        sb.AppendLine();
        sb.AppendLine("| Object | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");
        foreach (var obj in this.DifferencesByRootObject.OrderByDescending(o => o.Value.Count))
        {
            sb.AppendLine($"| {obj.Key} | {obj.Value.Count} | {this.RootObjectPercentages[obj.Key]}% |");
        }

        sb.AppendLine();

        // Common patterns
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();
        foreach (var pattern in this.CommonPatterns.Take(10)) // Top 10 patterns
        {
            sb.AppendLine($"### Pattern: {pattern.Pattern} ({pattern.OccurrenceCount} occurrences)");
            sb.AppendLine();
            sb.AppendLine("Example differences:");
            sb.AppendLine();
            foreach (var example in pattern.Examples)
            {
                sb.AppendLine($"- Property: `{example.PropertyName}`");
                sb.AppendLine($"  - Old: `{this.FormatValue(example.Object1Value)}`");
                sb.AppendLine($"  - New: `{this.FormatValue(example.Object2Value)}`");
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private string FormatCategoryName(DifferenceCategory category)
    {
        switch (category)
        {
            case DifferenceCategory.NumericValueChanged:
                return "Numeric Value Changed";
            case DifferenceCategory.DateTimeChanged:
                return "Date/Time Changed";
            case DifferenceCategory.BooleanValueChanged:
                return "Boolean Value Changed";
            case DifferenceCategory.CollectionItemChanged:
                return "Collection Item Changed";
            case DifferenceCategory.ItemAdded:
                return "Item Added";
            case DifferenceCategory.ItemRemoved:
                return "Item Removed";
            case DifferenceCategory.NullValueChange:
                return "Null Value Change";
            case DifferenceCategory.ValueChanged:
                return "Value Changed";
            default:
                return "Other";
        }
    }

    private string FormatValue(object value)
    {
        if (value == null) {
            return "null";
        }

        if (value is DateTime dt) {
            return dt.ToString("yyyy-MM-dd HH:mm:ss");
        }

        if (value is string str && str.Length > 50) {
            return str.Substring(0, 47) + "...";
        }

        return value.ToString();
    }
}
