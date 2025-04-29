using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Categorizes and summarizes differences from CompareNETObjects
/// </summary>
public class DifferenceCategorizer
{
    private readonly ILogger logger;
    public DifferenceCategorizer(ILogger logger = null)
    {
        this.logger = logger;
    }

    /// <summary>
    /// Creates a structured summary from CompareNETObjects comparison result
    /// </summary>
    public DifferenceSummary CategorizeAndSummarize(ComparisonResult comparisonResult)
    {
        logger?.LogInformation("Starting difference summary. AreEqual={AreEqual}, DifferenceCount={Count}",
            comparisonResult.AreEqual, comparisonResult.Differences?.Count ?? 0);

        var summary = new DifferenceSummary();

        if (comparisonResult.AreEqual)
        {
            summary.AreEqual = true;
            logger?.LogInformation("Objects are equal. No differences to categorize.");
            return summary;
        }

        summary.AreEqual = false;
        summary.TotalDifferenceCount = comparisonResult.Differences.Count;

        // Categorize differences by type
        CategorizeByChangeType(comparisonResult.Differences, summary);

        // Categorize differences by property path patterns
        CategorizeByPathPattern(comparisonResult.Differences, summary);

        // Categorize by root object
        CategorizeByRootObject(comparisonResult.Differences, summary);

        // Create category statistics
        CalculateStatistics(summary);

        logger?.LogInformation("Completed difference summary. Categories: {CategoryCount}, Patterns: {PatternCount}, RootObjects: {RootObjectCount}",
            summary.DifferencesByChangeType.Count,
            summary.CommonPatterns?.Count ?? 0,
            summary.DifferencesByRootObject.Count);

        return summary;
    }

    private void CategorizeByChangeType(List<Difference> differences, DifferenceSummary summary)
    {
        // Categorize by value type changes
        foreach (var diff in differences)
        {
            DifferenceCategory category;

            // Check if it's a collection difference
            if (diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]"))
            {
                if (diff.Object1Value == null && diff.Object2Value != null)
                    category = DifferenceCategory.ItemAdded;
                else if (diff.Object1Value != null && diff.Object2Value == null)
                    category = DifferenceCategory.ItemRemoved;
                else
                    category = DifferenceCategory.CollectionItemChanged;
            }
            // Check value type differences
            else if (IsNumericDifference(diff.Object1Value, diff.Object2Value))
            {
                category = DifferenceCategory.NumericValueChanged;
            }
            else if (IsDateTimeDifference(diff.Object1Value, diff.Object2Value))
            {
                category = DifferenceCategory.DateTimeChanged;
            }
            else if (IsStringDifference(diff.Object1Value, diff.Object2Value))
            {
                category = DifferenceCategory.TextContentChanged;
            }
            else if (IsBooleanDifference(diff.Object1Value, diff.Object2Value))
            {
                category = DifferenceCategory.BooleanValueChanged;
            }
            else if (diff.Object1Value == null || diff.Object2Value == null)
            {
                category = DifferenceCategory.NullValueChange;
            }
            else
            {
                category = DifferenceCategory.Other;
            }

            if (!summary.DifferencesByChangeType.ContainsKey(category))
            {
                summary.DifferencesByChangeType[category] = new List<Difference>();
            }

            summary.DifferencesByChangeType[category].Add(diff);
        }
        logger?.LogDebug("Categorized differences into {CategoryCount} categories.", summary.DifferencesByChangeType.Count);
    }

    private void CategorizeByPathPattern(List<Difference> differences, DifferenceSummary summary)
    {
        var patternGroups = new Dictionary<string, List<Difference>>();

        foreach (var diff in differences)
        {
            string pattern = GetPathPattern(diff.PropertyName);

            if (!patternGroups.ContainsKey(pattern))
            {
                patternGroups[pattern] = new List<Difference>();
            }

            patternGroups[pattern].Add(diff);
        }

        // Get the most significant patterns (with multiple occurrences)
        foreach (var group in patternGroups.Where(g => g.Value.Count > 1))
        {
            summary.CommonPatterns.Add(new DifferencePattern
            {
                Pattern = group.Key,
                PropertyPath = group.Key,
                OccurrenceCount = group.Value.Count,
                Examples = group.Value.Take(3).ToList()
            });
        }

        summary.CommonPatterns = summary.CommonPatterns
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();
        logger?.LogDebug("Identified {PatternCount} common property path patterns.", summary.CommonPatterns.Count);
    }

    private void CategorizeByRootObject(List<Difference> differences, DifferenceSummary summary)
    {
        foreach (var diff in differences)
        {
            string rootObject = GetRootObjectName(diff.PropertyName);
            DifferenceCategory category = GetDifferenceCategory(diff);

            if (!summary.DifferencesByRootObject.ContainsKey(rootObject))
            {
                summary.DifferencesByRootObject[rootObject] = new List<Difference>();
            }
            summary.DifferencesByRootObject[rootObject].Add(diff);

            if (!summary.DifferencesByRootObjectAndCategory.ContainsKey(rootObject))
            {
                summary.DifferencesByRootObjectAndCategory[rootObject] = new Dictionary<DifferenceCategory, List<Difference>>();
            }
            if (!summary.DifferencesByRootObjectAndCategory[rootObject].ContainsKey(category))
            {
                summary.DifferencesByRootObjectAndCategory[rootObject][category] = new List<Difference>();
            }
            summary.DifferencesByRootObjectAndCategory[rootObject][category].Add(diff);
        }
        logger?.LogDebug("Categorized differences into {RootObjectCount} root objects.", summary.DifferencesByRootObject.Count);
    }

    private void CalculateStatistics(DifferenceSummary summary)
    {
        // Calculate percentages for each category
        foreach (var category in summary.DifferencesByChangeType)
        {
            double percentage = (double)category.Value.Count / summary.TotalDifferenceCount * 100;
            summary.CategoryPercentages[category.Key] = Math.Round(percentage, 1);
        }

        // Calculate percentages for each root object
        foreach (var rootObj in summary.DifferencesByRootObject)
        {
            double percentage = (double)rootObj.Value.Count / summary.TotalDifferenceCount * 100;
            summary.RootObjectPercentages[rootObj.Key] = Math.Round(percentage, 1);
        }
        logger?.LogDebug("Calculated statistics for {CategoryCount} categories and {RootObjectCount} root objects.", summary.CategoryPercentages.Count, summary.RootObjectPercentages.Count);
    }

    private bool IsNumericDifference(object value1, object value2)
    {
        return (value1 is int || value1 is long || value1 is float || value1 is double || value1 is decimal) &&
               (value2 is int || value2 is long || value2 is float || value2 is double || value2 is decimal);
    }

    private bool IsDateTimeDifference(object value1, object value2)
    {
        return value1 is DateTime && value2 is DateTime;
    }

    private bool IsStringDifference(object value1, object value2)
    {
        return value1 is string && value2 is string;
    }

    private bool IsBooleanDifference(object value1, object value2)
    {
        return value1 is bool && value2 is bool;
    }

    private string GetPathPattern(string propertyPath)
    {
        // Replace array indices with [*] to generalize the pattern
        return Regex.Replace(propertyPath, @"\[\d+\]", "[*]");
    }

    private string GetRootObjectName(string propertyPath)
    {
        // Get the first segment of the property path
        int dotIndex = propertyPath.IndexOf('.');
        int bracketIndex = propertyPath.IndexOf('[');

        if (dotIndex == -1 && bracketIndex == -1)
            return propertyPath;

        if (dotIndex == -1)
            return propertyPath.Substring(0, bracketIndex);

        if (bracketIndex == -1)
            return propertyPath.Substring(0, dotIndex);

        return propertyPath.Substring(0, Math.Min(dotIndex, bracketIndex));
    }

    private DifferenceCategory GetDifferenceCategory(Difference diff)
    {
        // Check if it's a collection difference
        if (diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]"))
        {
            if (diff.Object1Value == null && diff.Object2Value != null)
                return DifferenceCategory.ItemAdded;
            else if (diff.Object1Value != null && diff.Object2Value == null)
                return DifferenceCategory.ItemRemoved;
            else
                return DifferenceCategory.CollectionItemChanged;
        }
        else if (diff.Object1Value is string || diff.Object2Value is string)
        {
            return DifferenceCategory.TextContentChanged;
        }
        else if (diff.Object1Value is double || diff.Object2Value is double ||
                 diff.Object1Value is float || diff.Object2Value is float ||
                 diff.Object1Value is int || diff.Object2Value is int ||
                 diff.Object1Value is long || diff.Object2Value is long)
        {
            return DifferenceCategory.NumericValueChanged;
        }
        else if (diff.Object1Value is DateTime || diff.Object2Value is DateTime)
        {
            return DifferenceCategory.DateTimeChanged;
        }
        else if (diff.Object1Value is bool || diff.Object2Value is bool)
        {
            return DifferenceCategory.BooleanValueChanged;
        }
        else if (diff.Object1Value == null || diff.Object2Value == null)
        {
            return DifferenceCategory.NullValueChange;
        }
        else
        {
            return DifferenceCategory.Other;
        }
    }
}