// <copyright file="DifferenceSummary.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

using System.Globalization;
using System.Text;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Summary of the comparison differences.
/// </summary>
public class DifferenceSummary
{
    public bool AreEqual
    {
        get; set;
    }

    public int TotalDifferenceCount
    {
        get; set;
    }

    public IDictionary<DifferenceCategory, IList<Difference>> DifferencesByChangeType { get; set; } = new Dictionary<DifferenceCategory, IList<Difference>>();

    public IDictionary<string, IList<Difference>> DifferencesByRootObject { get; set; } = new Dictionary<string, IList<Difference>>(StringComparer.Ordinal);

    public IDictionary<string, IDictionary<DifferenceCategory, IList<Difference>>> DifferencesByRootObjectAndCategory { get; set; } = new Dictionary<string, IDictionary<DifferenceCategory, IList<Difference>>>(StringComparer.Ordinal);

    public IDictionary<DifferenceCategory, double> CategoryPercentages { get; set; } = new Dictionary<DifferenceCategory, double>();

    public IDictionary<string, double> RootObjectPercentages { get; set; } = new Dictionary<string, double>(StringComparer.Ordinal);

    public IList<DifferencePattern> CommonPatterns { get; set; } = new List<DifferencePattern>();

    /// <summary>
    /// Generate a human-friendly summary report.
    /// </summary>
    /// <returns></returns>
    public string GenerateReport()
    {
        var sb = new StringBuilder();

        sb.AppendLine("# Comparison Summary Report");
        sb.AppendLine();

        if (AreEqual)
        {
            sb.AppendLine("**No differences found.** The objects are identical according to current comparison rules.");
            return sb.ToString();
        }

        AppendLineInvariant(sb, "**Total Differences: {0}**", TotalDifferenceCount);
        sb.AppendLine();

        // Summary by category
        sb.AppendLine("## Differences by Category");
        sb.AppendLine();
        sb.AppendLine("| Category | Count | Percentage |");
        sb.AppendLine("|----------|-------|------------|");

        foreach (var category in DifferencesByChangeType.OrderByDescending(c => c.Value.Count))
        {
            AppendLineInvariant(
                sb,
                "| {0} | {1} | {2}% |",
                FormatCategoryName(category.Key),
                category.Value.Count,
                CategoryPercentages[category.Key]);
        }

        sb.AppendLine();

        // Summary by root object and category
        sb.AppendLine("## Differences by Root Object and Category");
        sb.AppendLine();
        foreach (var obj in DifferencesByRootObjectAndCategory.OrderByDescending(o => o.Value.SelectMany(v => v.Value).Count()))
        {
            var total = obj.Value.SelectMany(v => v.Value).Count();
            AppendLineInvariant(sb, "### {0} (Total: {1})", obj.Key, total);
            foreach (var cat in obj.Value.OrderByDescending(c => c.Value.Count))
            {
                AppendLineInvariant(sb, "- {0}: {1}", FormatCategoryName(cat.Key), cat.Value.Count);

                // Show up to 5 examples
                foreach (var diff in cat.Value.Take(5))
                {
                    sb.AppendLine($"    - Property: `{diff.PropertyName}` | Old: `{FormatValue(diff.Object1Value)}` | New: `{FormatValue(diff.Object2Value)}`");
                }

                if (cat.Value.Count > 5)
                {
                    AppendLineInvariant(sb, "    ...and {0} more", cat.Value.Count - 5);
                }
            }

            sb.AppendLine();
        }

        // Fallback: Summary by root object (legacy)
        sb.AppendLine("## Differences by Root Object (Legacy)");
        sb.AppendLine();
        sb.AppendLine("| Object | Count | Percentage |");
        sb.AppendLine("|--------|-------|------------|");
        foreach (var obj in DifferencesByRootObject.OrderByDescending(o => o.Value.Count))
        {
            AppendLineInvariant(
                sb,
                "| {0} | {1} | {2}% |",
                obj.Key,
                obj.Value.Count,
                RootObjectPercentages[obj.Key]);
        }

        sb.AppendLine();

        // Common patterns
        sb.AppendLine("## Common Difference Patterns");
        sb.AppendLine();

        // Top 10 patterns
        foreach (var pattern in CommonPatterns.Take(10))
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

        return sb.ToString();
    }

    private static void AppendLineInvariant(StringBuilder sb, string format, params object[] args)
    {
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture, format, args));
    }

    private static string FormatCategoryName(DifferenceCategory category)
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

    private static string FormatValue(object value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is DateTime dt)
        {
            return dt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
        }

        if (value is string str && str.Length > 50)
        {
            return str.Substring(0, 47) + "...";
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture)
            : value.ToString() ?? string.Empty;
    }
}
