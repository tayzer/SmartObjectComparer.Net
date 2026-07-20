using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.Utilities;

namespace ComparisonTool.Cli.Reporting;

/// <summary>
/// Aggregates field-level difference statistics used by CLI report exports.
/// </summary>
public static class MostAffectedFieldsAggregator
{
    /// <summary>
    /// Computes common field differences across all non-error file pairs.
    /// </summary>
    public static MostAffectedFieldsSummary Build(MultiFolderComparisonResult result)
    {
        var fieldStats = new Dictionary<string, FieldStats>(StringComparer.Ordinal);
        var excludedRawTextPairCount = 0;
        var structuredPairCount = 0;

        for (var pairIndex = 0; pairIndex < result.FilePairResults.Count; pairIndex++)
        {
            var pair = result.FilePairResults[pairIndex];
            if (pair.HasError)
            {
                continue;
            }

            var differences = pair.Result?.Differences;
            var hasStructuredDifferences = differences != null && differences.Count > 0;
            var hasRawTextDifferences = pair.RawTextDifferences != null && pair.RawTextDifferences.Count > 0;

            if (!hasStructuredDifferences)
            {
                if (hasRawTextDifferences)
                {
                    excludedRawTextPairCount++;
                }

                continue;
            }

            structuredPairCount++;
            var fieldsSeenInPair = new HashSet<string>(StringComparer.Ordinal);

            foreach (var diff in differences!)
            {
                var selectedFieldPath = SelectGroupingPath(diff.PropertyName);
                if (string.IsNullOrWhiteSpace(selectedFieldPath))
                {
                    continue;
                }

                if (!fieldStats.TryGetValue(selectedFieldPath, out var stats))
                {
                    stats = new FieldStats();
                    fieldStats[selectedFieldPath] = stats;
                }

                stats.OccurrenceCount++;

                if (fieldsSeenInPair.Add(selectedFieldPath))
                {
                    stats.AffectedPairCount++;
                }
            }
        }

        var sortedFields = fieldStats
            .Select(kvp => new MostAffectedField
            {
                FieldPath = kvp.Key,
                AffectedPairCount = kvp.Value.AffectedPairCount,
                OccurrenceCount = kvp.Value.OccurrenceCount,
            })
            .OrderByDescending(field => field.AffectedPairCount)
            .ThenByDescending(field => field.OccurrenceCount)
            .ThenBy(field => field.FieldPath, StringComparer.Ordinal)
            .ToList();

        return new MostAffectedFieldsSummary
        {
            Fields = sortedFields,
            ExcludedRawTextPairCount = excludedRawTextPairCount,
            StructuredPairCount = structuredPairCount,
        };
    }

    private static string SelectGroupingPath(string? propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return string.Empty;
        }

        var normalizedPath = PropertyPathNormalizer.NormalizePropertyPath(propertyPath).Trim();
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            return string.Empty;
        }

        if (!RequiresParentFallback(normalizedPath))
        {
            return normalizedPath;
        }

        var parentPath = GetParentPath(normalizedPath);
        return string.IsNullOrWhiteSpace(parentPath) ? normalizedPath : parentPath;
    }

    private static bool RequiresParentFallback(string normalizedPath)
    {
        return normalizedPath.Contains("[*]", StringComparison.Ordinal);
    }

    private static string GetParentPath(string normalizedPath)
    {
        if (normalizedPath.EndsWith("[*]", StringComparison.Ordinal) && normalizedPath.Length > 3)
        {
            return normalizedPath[..^3];
        }

        var lastSegmentSeparator = normalizedPath.LastIndexOf('.');
        if (lastSegmentSeparator <= 0)
        {
            return normalizedPath;
        }

        return normalizedPath[..lastSegmentSeparator];
    }

    private sealed class FieldStats
    {
        public int AffectedPairCount { get; set; }

        public int OccurrenceCount { get; set; }
    }
}