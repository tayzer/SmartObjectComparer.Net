using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

/// <summary>Builds report analysis while detail pages are streamed, without retaining every pair.</summary>
public sealed class IncrementalRunAnalysisBuilder
{
    private const int TopLimit = 25;
    private readonly Dictionary<string, (int Occurrences, int Pairs)> categories = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<RequestPairResult> topCandidates = new();
    private int totalPairs;
    private int completedPairs;
    private int differentPairs;
    private int errorPairs;
    private int totalDifferences;

    public void Add(RequestPairResult item)
    {
        totalPairs++;
        completedPairs += item.Outcome != RequestPairOutcome.ExecutionFailed ? 1 : 0;
        differentPairs += item.Outcome is RequestPairOutcome.Different or RequestPairOutcome.StatusCodeMismatch or RequestPairOutcome.BothNonSuccess ? 1 : 0;
        errorPairs += item.Outcome == RequestPairOutcome.ExecutionFailed ? 1 : 0;
        totalDifferences += item.DifferenceCount;
        foreach (string category in CategoriesFor(item).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int occurrences = Math.Max(1, item.Differences.Count(difference => string.Equals(StaticReportDifferenceIndexBuilder.CategorizeDifference(difference), category, StringComparison.OrdinalIgnoreCase)));
            categories.TryGetValue(category, out (int Occurrences, int Pairs) current);
            categories[category] = (current.Occurrences + occurrences, current.Pairs + 1);
        }

        if (item.Outcome != RequestPairOutcome.Equal)
        {
            topCandidates.Add(item);
            if (topCandidates.Count > TopLimit * 2)
            {
                TrimTopCandidates();
            }
        }
    }

    public StaticReportAnalysisSnapshot Build(string? differenceIndexPath)
    {
        TrimTopCandidates();
        return new StaticReportAnalysisSnapshot(
            totalPairs, completedPairs, differentPairs, errorPairs, totalDifferences,
            categories.OrderByDescending(entry => entry.Value.Occurrences).ThenBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                .Select(entry => new StaticReportDifferenceCategorySummary(entry.Key, entry.Key, entry.Value.Occurrences, entry.Value.Pairs)).ToList(),
            topCandidates.Select(item => new StaticReportAffectedObjectSummary(item.RelativePath, Math.Max(item.DifferenceCount, item.Differences.Count), CategoriesFor(item).FirstOrDefault() ?? "Other", item.Outcome.ToString())).ToList(),
            differenceIndexPath);
    }

    private void TrimTopCandidates()
    {
        if (topCandidates.Count > TopLimit)
        {
            topCandidates.Sort((left, right) =>
            {
                int count = Math.Max(right.DifferenceCount, right.Differences.Count).CompareTo(Math.Max(left.DifferenceCount, left.Differences.Count));
                return count != 0 ? count : StringComparer.OrdinalIgnoreCase.Compare(left.RelativePath, right.RelativePath);
            });
            topCandidates.RemoveRange(TopLimit, topCandidates.Count - TopLimit);
        }
    }

    private static IEnumerable<string> CategoriesFor(RequestPairResult item)
    {
        if (item.Outcome == RequestPairOutcome.Equal) { yield return "Equal"; yield break; }
        if (item.Outcome == RequestPairOutcome.ExecutionFailed) { yield return "Errors"; yield break; }
        if (item.Outcome == RequestPairOutcome.StatusCodeMismatch) yield return "HTTP Status";
        if (item.Outcome == RequestPairOutcome.BothNonSuccess) yield return "Non-Success";
        foreach (Comparison.ComparisonDifference difference in item.Differences) yield return StaticReportDifferenceIndexBuilder.CategorizeDifference(difference);
    }
}
