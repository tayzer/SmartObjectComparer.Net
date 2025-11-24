// <copyright file="DifferenceFilter.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Utilities
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using ComparisonTool.Core.Comparison.Results;
    using ComparisonTool.Core.Utilities;
    using KellermanSoftware.CompareNetObjects;
    using Microsoft.Extensions.Logging;

    /// <summary>
    /// Helper to normalize and remove duplicate differences from CompareNETObjects results.
    /// Extracted from ComparisonService.FilterDuplicateDifferences to allow reuse (cache, UI, etc.).
    /// </summary>
    public static class DifferenceFilter
    {
        public static ComparisonResult FilterDuplicateDifferences(ComparisonResult result, ILogger logger = null)
        {
            if (result == null) return result;

            if (result.Differences == null || result.Differences.Count <= 1) {
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

        private static bool IsConfusingCollectionDifference(Difference diff, ILogger logger)
        {
            if (diff == null) return false;

            if (diff.PropertyName.EndsWith(".System.Collections.IList.Item") || diff.PropertyName.EndsWith(".System.Collections.Generic.IList`1.Item"))
            {
                if (int.TryParse(diff.Object1Value?.ToString(), out _) && int.TryParse(diff.Object2Value?.ToString(), out _))
                {
                    logger?.LogDebug("Filtering out confusing collection count difference: '{PropertyName}' (Old: '{OldValue}', New: '{NewValue}')", diff.PropertyName, diff.Object1Value, diff.Object2Value);
                    return true;
                }
            }

            return false;
        }

        private static Difference ImproveDifferenceDescription(Difference diff, ILogger logger)
        {
            if (diff == null) return diff;

            if (diff.PropertyName.Contains(".System.Collections.IList.Item[") && diff.Object1Value?.ToString() == "(null)" && diff.Object2Value?.ToString()?.Contains('.') == true)
            {
                var indexMatch = System.Text.RegularExpressions.Regex.Match(diff.PropertyName, @"\[(\d+)\]$");
                if (indexMatch.Success)
                {
                    var index = indexMatch.Groups[1].Value;
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
                OldValue = diff.Object1Value?.ToString() ?? "null",
                NewValue = diff.Object2Value?.ToString() ?? "null",
                PropertyPath = normalizedPath,
            };
        }

        // Internal key copied from ComparisonService for grouping
        private class DifferenceGroupingKey
        {
            public string OldValue { get; set; }

            public string NewValue { get; set; }

            public string PropertyPath { get; set; }

            public override bool Equals(object obj)
            {
                if (obj is not DifferenceGroupingKey other) return false;
                return string.Equals(this.OldValue, other.OldValue, StringComparison.Ordinal) && string.Equals(this.NewValue, other.NewValue, StringComparison.Ordinal) && string.Equals(this.PropertyPath, other.PropertyPath, StringComparison.Ordinal);
            }

            public override int GetHashCode()
            {
                return HashCode.Combine(this.OldValue?.GetHashCode() ?? 0, this.NewValue?.GetHashCode() ?? 0, this.PropertyPath?.GetHashCode() ?? 0);
            }
        }
    }
}
