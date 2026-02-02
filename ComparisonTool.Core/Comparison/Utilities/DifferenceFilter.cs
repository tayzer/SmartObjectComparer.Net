using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison.Utilities;

/// <summary>
/// Helper to normalize and remove duplicate differences from CompareNETObjects results.
/// Extracted from ComparisonService.FilterDuplicateDifferences to allow reuse (cache, UI, etc.).
/// </summary>
public static class DifferenceFilter
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static ComparisonResult FilterDuplicateDifferences(ComparisonResult result, ILogger? logger = null)
    {
        if (result == null)
        {
            throw new ArgumentNullException(nameof(result));
        }

        if (result.Differences == null || result.Differences.Count <= 1)
        {
            return result;
        }

        try
        {
            var originalCount = result.Differences.Count;
            logger?.LogDebug("Filtering duplicate differences. Original count: {OriginalCount}", originalCount);

            var filteredDifferences = result.Differences.Where(diff => !IsConfusingCollectionDifference(diff, logger)).ToList();

            var improvedDifferences = filteredDifferences.Select(diff => ImproveDifferenceDescription(diff, logger)).ToList();

            var groups = improvedDifferences.GroupBy(d => CreateGroupingKey(d)).ToList();

            var uniqueDiffs = groups.Select(group =>
            {
                var bestMatch = group
                    .OrderBy(d => d.PropertyName.Contains("k__BackingField") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Contains("System.Collections.IList.Item") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Contains("System.Collections.Generic.IList`1.Item") ? 1 : 0)
                    .ThenBy(d => d.PropertyName.Length)
                    .First();

                if (group.Count() > 1)
                {
                    logger?.LogDebug("Found duplicate group with {Count} items. Property path: {PropertyPath}. Selected: {SelectedPath}", group.Count(), group.Key.PropertyPath, bestMatch.PropertyName);
                }

                return bestMatch;
            }).ToList();

            result.Differences.Clear();
            result.Differences.AddRange(uniqueDiffs);
            logger?.LogDebug("Duplicate filtering complete. Original: {OriginalCount}, Filtered: {FilteredCount}, Removed: {RemovedCount}", originalCount, result.Differences.Count, originalCount - result.Differences.Count);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Error while filtering duplicate differences");
        }

        return result;
    }

    private static bool IsConfusingCollectionDifference(Difference diff, ILogger? logger)
    {
        if (diff.PropertyName.EndsWith(".System.Collections.IList.Item", StringComparison.Ordinal) || diff.PropertyName.EndsWith(".System.Collections.Generic.IList`1.Item", StringComparison.Ordinal))
        {
            if (int.TryParse(ToInvariantString(diff.Object1Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
                && int.TryParse(ToInvariantString(diff.Object2Value), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                logger?.LogDebug("Filtering out confusing collection count difference: '{PropertyName}' (Old: '{OldValue}', New: '{NewValue}')", diff.PropertyName, diff.Object1Value, diff.Object2Value);
                return true;
            }
        }

        return false;
    }

    private static Difference ImproveDifferenceDescription(Difference diff, ILogger? logger)
    {
        if (diff == null)
        {
            throw new ArgumentNullException(nameof(diff));
        }

        if (diff.PropertyName.Contains(".System.Collections.IList.Item[")
            && string.Equals(ToInvariantString(diff.Object1Value), "(null)", StringComparison.Ordinal)
            && ToInvariantString(diff.Object2Value).Contains(".", StringComparison.Ordinal))
        {
            var indexMatch = Regex.Match(
                diff.PropertyName,
                @"\[(?<index>\d+)\]$",
                RegexOptions.ExplicitCapture,
                RegexTimeout);
            if (indexMatch.Success)
            {
                var index = indexMatch.Groups["index"].Value;
                var basePath = diff.PropertyName.Replace($".System.Collections.IList.Item[{index}]", string.Empty);
                var improvedPropertyName = $"{basePath}[{index}] (New Element)";
                logger?.LogDebug("Improving null element difference: '{Original}' -> '{Improved}'", diff.PropertyName, improvedPropertyName);

                return new Difference
                {
                    PropertyName = improvedPropertyName,
                    Object1Value = diff.Object1Value,
                    Object2Value = diff.Object2Value,
                };
            }
        }

        return diff;
    }

    private static DifferenceGroupingKey CreateGroupingKey(Difference diff)
    {
        var normalizedPath = PropertyPathNormalizer.NormalizePropertyPath(diff.PropertyName);

        return new DifferenceGroupingKey
        {
            OldValue = ToInvariantString(diff.Object1Value),
            NewValue = ToInvariantString(diff.Object2Value),
            PropertyPath = normalizedPath,
        };
    }

    private static string ToInvariantString(object? value)
    {
        if (value is null)
        {
            return "null";
        }

        return value is IFormattable formattable
            ? formattable.ToString(null, CultureInfo.InvariantCulture) ?? "null"
            : value.ToString() ?? "null";
    }

    // Internal key copied from ComparisonService for grouping
    private class DifferenceGroupingKey
    {
        required public string OldValue
        {
            get; set;
        }

        required public string NewValue
        {
            get; set;
        }

        required public string PropertyPath
        {
            get; set;
        }

        public override bool Equals(object? obj)
        {
            if (obj is not DifferenceGroupingKey other)
            {
                return false;
            }

            return string.Equals(OldValue, other.OldValue, StringComparison.Ordinal) && string.Equals(NewValue, other.NewValue, StringComparison.Ordinal) && string.Equals(PropertyPath, other.PropertyPath, StringComparison.Ordinal);
        }

        public override int GetHashCode() => HashCode.Combine(
            StringComparer.Ordinal.GetHashCode(OldValue ?? string.Empty),
            StringComparer.Ordinal.GetHashCode(NewValue ?? string.Empty),
            StringComparer.Ordinal.GetHashCode(PropertyPath ?? string.Empty));
    }
}
