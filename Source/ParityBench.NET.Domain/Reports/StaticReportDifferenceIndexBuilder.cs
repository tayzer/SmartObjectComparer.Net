using System.Text.RegularExpressions;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

public static class StaticReportDifferenceIndexBuilder
{
    private static readonly Regex CollectionIndexRegex = new Regex(@"\[\d+\]", RegexOptions.Compiled);

    public static StaticReportDifferenceIndex Build(IEnumerable<RequestPairResult> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        List<StaticReportAffectedPairDifference> rows = new List<StaticReportAffectedPairDifference>();
        foreach (RequestPairResult item in items)
        {
            foreach (ComparisonDifference difference in item.Differences)
            {
                if (!IsStructuredDomainDifference(difference))
                {
                    continue;
                }

                string normalizedPath = NormalizePropertyPath(difference.PropertyPath);
                string category = CategorizeDifference(difference);
                rows.Add(new StaticReportAffectedPairDifference(
                    item.RelativePath,
                    difference.PropertyPath,
                    normalizedPath,
                    category,
                    1,
                    item.Outcome,
                    item.ResponseA?.StatusCode,
                    item.ResponseB?.StatusCode,
                    difference.ValueA,
                    difference.ValueB,
                    difference.Message));
            }
        }

        IReadOnlyList<StaticReportPropertyDifferenceSummary> properties = rows
            .GroupBy(row => row.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                List<StaticReportAffectedPairDifference> affectedPairs = group
                    .GroupBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(pairGroup => MergePairRows(pairGroup))
                    .OrderBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();
                StaticReportAffectedPairDifference first = group.First();
                return new StaticReportPropertyDifferenceSummary(
                    first.PropertyPath,
                    group.Key,
                    group.Select(row => row.Category).GroupBy(category => category, StringComparer.OrdinalIgnoreCase).OrderByDescending(category => category.Count()).ThenBy(category => category.Key, StringComparer.OrdinalIgnoreCase).First().Key,
                    group.Sum(row => row.OccurrenceCount),
                    affectedPairs.Count,
                    affectedPairs);
            })
            .OrderByDescending(property => property.OccurrenceCount)
            .ThenBy(property => property.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new StaticReportDifferenceIndex(
            rows.Sum(row => row.OccurrenceCount),
            rows.Select(row => row.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
            properties);
    }

    public static string NormalizePropertyPath(string propertyPath)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            return "Response";
        }

        return CollectionIndexRegex.Replace(propertyPath.Trim(), "[*]");
    }

    public static string CategorizeDifference(ComparisonDifference difference)
    {
        string path = difference.PropertyPath ?? string.Empty;
        string message = difference.Message ?? string.Empty;
        if (string.Equals(path, "HttpStatus", StringComparison.OrdinalIgnoreCase))
        {
            return "HTTP Status";
        }

        if (path.StartsWith("Body.", StringComparison.OrdinalIgnoreCase))
        {
            return "Raw Body";
        }

        if (message.Contains("missing", StringComparison.OrdinalIgnoreCase)
            || message.Contains("null", StringComparison.OrdinalIgnoreCase)
            || difference.ValueA is null
            || difference.ValueB is null)
        {
            return "Missing Properties";
        }

        if (path.Contains('[', StringComparison.Ordinal) || message.Contains("collection", StringComparison.OrdinalIgnoreCase))
        {
            return "Collection / Order";
        }

        if (message.Contains("type", StringComparison.OrdinalIgnoreCase))
        {
            return "Critical";
        }

        return "Value Differences";
    }

    internal static bool IsStructuredDomainDifference(ComparisonDifference difference)
    {
        string path = difference.PropertyPath ?? string.Empty;
        return !string.Equals(path, "HttpStatus", StringComparison.OrdinalIgnoreCase)
            && !path.StartsWith("Body.", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(path, "BodyPreview", StringComparison.OrdinalIgnoreCase);
    }

    internal static StaticReportAffectedPairDifference MergePairRows(IEnumerable<StaticReportAffectedPairDifference> rows)
    {
        List<StaticReportAffectedPairDifference> orderedRows = rows
            .OrderBy(row => row.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
        StaticReportAffectedPairDifference first = orderedRows[0];
        StaticReportAffectedPairDifference representative = orderedRows
            .OrderByDescending(row => HasValue(row.ValueA) || HasValue(row.ValueB))
            .ThenBy(row => row.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .First();

        return new StaticReportAffectedPairDifference(
            first.RelativePath,
            representative.PropertyPath,
            first.NormalizedPath,
            first.Category,
            orderedRows.Sum(row => row.OccurrenceCount),
            first.Outcome,
            first.StatusCodeA,
            first.StatusCodeB,
            representative.ValueA,
            representative.ValueB,
            representative.Message);
    }

    internal static bool HasValue(string? value) => !string.IsNullOrEmpty(value);
}

/// <summary>Incrementally builds the persisted difference index while detail pages stream.</summary>
public sealed class StaticReportDifferenceIndexAccumulator
{
    private readonly List<StaticReportAffectedPairDifference> rows = new();

    public void Add(RequestPairResult item)
    {
        foreach (ComparisonDifference difference in item.Differences)
        {
            if (!StaticReportDifferenceIndexBuilder.IsStructuredDomainDifference(difference)) continue;
            rows.Add(new StaticReportAffectedPairDifference(
                item.RelativePath, difference.PropertyPath,
                StaticReportDifferenceIndexBuilder.NormalizePropertyPath(difference.PropertyPath),
                StaticReportDifferenceIndexBuilder.CategorizeDifference(difference), 1, item.Outcome,
                item.ResponseA?.StatusCode, item.ResponseB?.StatusCode,
                difference.ValueA, difference.ValueB, difference.Message));
        }
    }

    public StaticReportDifferenceIndex Build()
    {
        IReadOnlyList<StaticReportPropertyDifferenceSummary> properties = rows
            .GroupBy(row => row.NormalizedPath, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                List<StaticReportAffectedPairDifference> affectedPairs = group.GroupBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase)
                    .Select(StaticReportDifferenceIndexBuilder.MergePairRows)
                    .OrderBy(row => row.RelativePath, StringComparer.OrdinalIgnoreCase).ToList();
                StaticReportAffectedPairDifference first = group.First();
                string category = group.Select(row => row.Category).GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(value => value.Count()).ThenBy(value => value.Key, StringComparer.OrdinalIgnoreCase).First().Key;
                return new StaticReportPropertyDifferenceSummary(first.PropertyPath, group.Key, category, group.Sum(row => row.OccurrenceCount), affectedPairs.Count, affectedPairs);
            })
            .OrderByDescending(property => property.OccurrenceCount)
            .ThenBy(property => property.NormalizedPath, StringComparer.OrdinalIgnoreCase).ToList();
        return new StaticReportDifferenceIndex(rows.Sum(row => row.OccurrenceCount), rows.Select(row => row.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count(), properties);
    }
}
