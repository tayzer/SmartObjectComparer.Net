// <copyright file="DifferenceCategorizer.cs" company="PlaceholderCompany">
using System;
using System.Text.RegularExpressions;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Categorizes and summarizes differences from CompareNETObjects.
/// </summary>
public class DifferenceCategorizer
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    private readonly ILogger logger;

    public DifferenceCategorizer(ILogger? logger = null) => this.logger = logger;

    /// <summary>
    /// Creates a structured summary from CompareNETObjects comparison result.
    /// </summary>
    /// <returns></returns>
    public DifferenceSummary CategorizeAndSummarize(ComparisonResult comparisonResult)
    {
        if (comparisonResult == null)
        {
            logger?.LogWarning("CategorizeAndSummarize called with null ComparisonResult");
            return new DifferenceSummary { AreEqual = true, TotalDifferenceCount = 0 };
        }

        var diffs = comparisonResult.Differences ?? new List<Difference>();

        logger?.LogInformation(
            "Starting difference summary. AreEqual={AreEqual}, DifferenceCount={Count}",
            comparisonResult.AreEqual,
            diffs.Count);

        var summary = new DifferenceSummary();

        if (comparisonResult.AreEqual)
        {
            summary.AreEqual = true;
            logger?.LogInformation("Objects are equal. No differences to categorize.");
            return summary;
        }

        summary.AreEqual = false;
        summary.TotalDifferenceCount = diffs.Count;

        // --- Single-pass grouping for efficiency ---
        var patternGroups = new Dictionary<string, List<Difference>>(StringComparer.Ordinal);
        foreach (var diff in diffs)
        {
            // Change type grouping
            var category = GetDifferenceCategory(diff);
            if (!summary.DifferencesByChangeType.TryGetValue(category, out var catList))
            {
                summary.DifferencesByChangeType[category] = catList = new List<Difference>();
            }

            catList.Add(diff);

            // Path pattern grouping
            var pattern = GetPathPattern(diff.PropertyName);
            if (!patternGroups.TryGetValue(pattern, out var patList))
            {
                patternGroups[pattern] = patList = new List<Difference>();
            }

            patList.Add(diff);

            // Root object grouping
            var rootObject = GetRootObjectName(diff.PropertyName);
            if (!summary.DifferencesByRootObject.TryGetValue(rootObject, out var rootList))
            {
                summary.DifferencesByRootObject[rootObject] = rootList = new List<Difference>();
            }

            rootList.Add(diff);

            // Root object + category grouping
            if (!summary.DifferencesByRootObjectAndCategory.TryGetValue(rootObject, out var rootCatDict))
            {
                summary.DifferencesByRootObjectAndCategory[rootObject] = rootCatDict = new Dictionary<DifferenceCategory, List<Difference>>();
            }

            if (!rootCatDict.TryGetValue(category, out var rootCatList))
            {
                rootCatDict[category] = rootCatList = new List<Difference>();
            }

            rootCatList.Add(diff);
        }

        logger?.LogDebug("Categorized differences into {CategoryCount} categories.", summary.DifferencesByChangeType.Count);
        logger?.LogDebug("Categorized differences into {RootObjectCount} root objects.", summary.DifferencesByRootObject.Count);

        // --- Pattern grouping post-processing ---
        foreach (var group in patternGroups)
        {
            if (group.Value.Count > 1)
            {
                summary.CommonPatterns.Add(new DifferencePattern
                {
                    Pattern = group.Key,
                    PropertyPath = group.Key,
                    OccurrenceCount = group.Value.Count,
                    Examples = group.Value.Take(3).ToList(),
                });
            }
        }

        summary.CommonPatterns = summary.CommonPatterns
            .OrderByDescending(p => p.OccurrenceCount)
            .ToList();
        logger?.LogDebug("Identified {PatternCount} common property path patterns.", summary.CommonPatterns.Count);

        // --- Statistics ---
        CalculateStatistics(summary);
        logger?.LogDebug("Calculated statistics for {CategoryCount} categories and {RootObjectCount} root objects.", summary.CategoryPercentages.Count, summary.RootObjectPercentages.Count);

        logger?.LogInformation(
            "Completed difference summary. Categories: {CategoryCount}, Patterns: {PatternCount}, RootObjects: {RootObjectCount}",
            summary.DifferencesByChangeType.Count,
            summary.CommonPatterns?.Count ?? 0,
            summary.DifferencesByRootObject.Count);

        return summary;
    }

    private void CalculateStatistics(DifferenceSummary summary)
    {
        // Calculate percentages for each category
        foreach (var category in summary.DifferencesByChangeType)
        {
            var percentage = (double)category.Value.Count / summary.TotalDifferenceCount * 100;
            summary.CategoryPercentages[category.Key] = Math.Round(percentage, 1);
        }

        // Calculate percentages for each root object
        foreach (var rootObj in summary.DifferencesByRootObject)
        {
            var percentage = (double)rootObj.Value.Count / summary.TotalDifferenceCount * 100;
            summary.RootObjectPercentages[rootObj.Key] = Math.Round(percentage, 1);
        }
    }

    private bool IsNumericDifference(object value1, object value2) =>
        (value1 is int || value1 is long || value1 is float || value1 is double || value1 is decimal) &&
        (value2 is int || value2 is long || value2 is float || value2 is double || value2 is decimal);

    private bool IsDateTimeDifference(object value1, object value2) => value1 is DateTime && value2 is DateTime;

    private bool IsStringDifference(object value1, object value2) => value1 is string && value2 is string;

    private bool IsBooleanDifference(object value1, object value2) => value1 is bool && value2 is bool;

    // Replace array indices with [*] to generalize the pattern
    private string GetPathPattern(string propertyPath) => Regex.Replace(propertyPath, @"\[\d+\]", "[*]", RegexOptions.None, RegexTimeout);

    private string GetRootObjectName(string propertyPath)
    {
        // For paths with collections, include the full path with normalized indices for better specificity
        // e.g., "Body.Response.Results[0].Details.Description" -> "Body.Response.Results[*].Details.Description"
        if (propertyPath.Contains("["))
        {
            // Replace specific indices with [*] and return the complete path to show exactly what property is affected
            var normalizedPath = Regex.Replace(propertyPath, @"\[\d+\]", "[*]", RegexOptions.None, RegexTimeout);
            return normalizedPath;
        }

        // For simple paths without collections, return the full path to be precise about what's changing
        // e.g., "Body.Response.SomeProperty" -> "Body.Response.SomeProperty"
        return propertyPath;
    }

    private DifferenceCategory GetDifferenceCategory(Difference diff)
    {
        // First check for null value changes
        if (diff.Object1Value == null || diff.Object2Value == null)
        {
            return DifferenceCategory.NullValueChange;
        }

        // Then categorize based on the actual value types, regardless of path structure
        if (IsNumericDifference(diff.Object1Value, diff.Object2Value))
        {
            return DifferenceCategory.NumericValueChanged;
        }
        else if (IsDateTimeDifference(diff.Object1Value, diff.Object2Value))
        {
            return DifferenceCategory.DateTimeChanged;
        }
        else if (IsStringDifference(diff.Object1Value, diff.Object2Value))
        {
            return DifferenceCategory.TextContentChanged;
        }
        else if (IsBooleanDifference(diff.Object1Value, diff.Object2Value))
        {
            return DifferenceCategory.BooleanValueChanged;
        }

        // Only categorize as collection changes if the path indicates actual collection structure changes
        // (not property changes within collection items)
        if (diff.PropertyName.Contains("[") && diff.PropertyName.Contains("]"))
        {
            // Check if this is actually a collection structure change vs property change within collection
            if (IsCollectionStructureChange(diff))
            {
                if (diff.Object1Value == null && diff.Object2Value != null)
                {
                    return DifferenceCategory.ItemAdded;
                }
                else if (diff.Object1Value != null && diff.Object2Value == null)
                {
                    return DifferenceCategory.ItemRemoved;
                }
                else
                {
                    return DifferenceCategory.CollectionItemChanged;
                }
            }

            // If it's a property within a collection item, fall through to value-based categorization
        }

        return DifferenceCategory.ValueChanged;
    }

    private bool IsCollectionStructureChange(Difference diff)
    {
        // This is a collection structure change if the property path ends with an index
        // e.g., "Results[0]" vs "Results[0].Description"
        var match = Regex.Match(diff.PropertyName, @"\[(\d+|\*)\]$", RegexOptions.None, RegexTimeout);
        return match.Success;
    }
}
