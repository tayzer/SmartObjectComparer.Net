using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core;

/// <summary>
/// Categorizes and summarizes differences from CompareNETObjects
/// </summary>
public class DifferenceCategorizer
{
    /// <summary>
    /// Creates a structured summary from CompareNETObjects comparison result
    /// </summary>
    public DifferenceSummary CategorizeAndSummarize(ComparisonResult comparisonResult)
    {
        var summary = new DifferenceSummary();

        if (comparisonResult.AreEqual)
        {
            summary.AreEqual = true;
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

            // Add to summary
            if (!summary.DifferencesByChangeType.ContainsKey(category))
            {
                summary.DifferencesByChangeType[category] = new List<Difference>();
            }

            summary.DifferencesByChangeType[category].Add(diff);
        }
    }

    private void CategorizeByPathPattern(List<Difference> differences, DifferenceSummary summary)
    {
        // Group by pattern to find common difference patterns
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
                Examples = group.Value.Take(3).ToList() // Take just a few examples
            });
        }

        // Sort by occurrence count
        summary.CommonPatterns = summary.CommonPatterns
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();
    }

    private void CategorizeByRootObject(List<Difference> differences, DifferenceSummary summary)
    {
        // Group by root object name
        foreach (var diff in differences)
        {
            string rootObject = GetRootObjectName(diff.PropertyName);

            if (!summary.DifferencesByRootObject.ContainsKey(rootObject))
            {
                summary.DifferencesByRootObject[rootObject] = new List<Difference>();
            }

            summary.DifferencesByRootObject[rootObject].Add(diff);
        }
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
    }

    #region Helper Methods

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

    #endregion
}