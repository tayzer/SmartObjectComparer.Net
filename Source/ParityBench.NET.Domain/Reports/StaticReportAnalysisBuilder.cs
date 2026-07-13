using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

public static class StaticReportAnalysisBuilder
{
    private const int TopAffectedObjectLimit = 25;

    public static StaticReportAnalysisSnapshot Build(
        IReadOnlyList<RequestPairResult> items,
        string? differenceIndexPath = null)
    {
        Dictionary<string, CategoryAccumulator> categories = new Dictionary<string, CategoryAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (RequestPairResult item in items)
        {
            List<string> pairCategories = GetPairCategories(item).ToList();
            if (pairCategories.Count == 0)
            {
                pairCategories.Add(item.Outcome == RequestPairOutcome.Equal ? "Equal" : "Other");
            }

            foreach (string category in pairCategories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!categories.TryGetValue(category, out CategoryAccumulator? accumulator))
                {
                    accumulator = new CategoryAccumulator(category);
                    categories.Add(category, accumulator);
                }

                accumulator.AffectedPairCount++;
                accumulator.OccurrenceCount += Math.Max(
                    1,
                    item.Differences.Count(difference => string.Equals(
                        StaticReportDifferenceIndexBuilder.CategorizeDifference(difference),
                        category,
                        StringComparison.OrdinalIgnoreCase)));
            }
        }

        IReadOnlyList<StaticReportDifferenceCategorySummary> categorySummaries = categories.Values
            .OrderByDescending(category => category.OccurrenceCount)
            .ThenBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
            .Select(category => new StaticReportDifferenceCategorySummary(
                category.Category,
                category.Category,
                category.OccurrenceCount,
                category.AffectedPairCount))
            .ToList();

        IReadOnlyList<StaticReportAffectedObjectSummary> affectedObjects = items
            .Where(item => item.Outcome != RequestPairOutcome.Equal)
            .OrderByDescending(item => Math.Max(item.DifferenceCount, item.Differences.Count))
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(TopAffectedObjectLimit)
            .Select(item => new StaticReportAffectedObjectSummary(
                item.RelativePath,
                Math.Max(item.DifferenceCount, item.Differences.Count),
                GetPairCategories(item).FirstOrDefault() ?? "Other",
                item.Outcome.ToString()))
            .ToList();

        return new StaticReportAnalysisSnapshot(
            items.Count,
            items.Count(item => item.Outcome != RequestPairOutcome.ExecutionFailed),
            items.Count(item => item.Outcome is RequestPairOutcome.Different or RequestPairOutcome.StatusCodeMismatch or RequestPairOutcome.BothNonSuccess),
            items.Count(item => item.Outcome == RequestPairOutcome.ExecutionFailed),
            items.Sum(item => item.DifferenceCount),
            categorySummaries,
            affectedObjects,
            differenceIndexPath);
    }

    private static IEnumerable<string> GetPairCategories(RequestPairResult item)
    {
        if (item.Outcome == RequestPairOutcome.Equal)
        {
            yield return "Equal";
            yield break;
        }

        if (item.Outcome == RequestPairOutcome.ExecutionFailed)
        {
            yield return "Errors";
            yield break;
        }

        if (item.Outcome == RequestPairOutcome.StatusCodeMismatch)
        {
            yield return "HTTP Status";
        }

        if (item.Outcome == RequestPairOutcome.BothNonSuccess)
        {
            yield return "Non-Success";
        }

        foreach (Comparison.ComparisonDifference difference in item.Differences)
        {
            yield return StaticReportDifferenceIndexBuilder.CategorizeDifference(difference);
        }
    }
}
