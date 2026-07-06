namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportAnalysisSnapshot
{
    public StaticReportAnalysisSnapshot(
        int totalPairs,
        int analyzedPairs,
        int differentPairs,
        int errorPairs,
        int totalDifferences,
        IReadOnlyList<StaticReportDifferenceCategorySummary>? categories = null,
        IReadOnlyList<StaticReportAffectedObjectSummary>? topAffectedObjects = null,
        string? differenceIndexPath = null)
    {
        EnsureNonNegative(totalPairs, nameof(totalPairs));
        EnsureNonNegative(analyzedPairs, nameof(analyzedPairs));
        EnsureNonNegative(differentPairs, nameof(differentPairs));
        EnsureNonNegative(errorPairs, nameof(errorPairs));
        EnsureNonNegative(totalDifferences, nameof(totalDifferences));

        TotalPairs = totalPairs;
        AnalyzedPairs = analyzedPairs;
        DifferentPairs = differentPairs;
        ErrorPairs = errorPairs;
        TotalDifferences = totalDifferences;
        Categories = (categories ?? Array.Empty<StaticReportDifferenceCategorySummary>()).ToList();
        TopAffectedObjects = (topAffectedObjects ?? Array.Empty<StaticReportAffectedObjectSummary>()).ToList();
        DifferenceIndexPath = string.IsNullOrWhiteSpace(differenceIndexPath) ? null : differenceIndexPath.Trim();
    }

    public int TotalPairs { get; }

    public int AnalyzedPairs { get; }

    public int DifferentPairs { get; }

    public int ErrorPairs { get; }

    public int TotalDifferences { get; }

    public IReadOnlyList<StaticReportDifferenceCategorySummary> Categories { get; }

    public IReadOnlyList<StaticReportAffectedObjectSummary> TopAffectedObjects { get; }

    public string? DifferenceIndexPath { get; }

    public IReadOnlyList<string> AvailableCategories =>
        Categories.Select(category => category.Category).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    private static void EnsureNonNegative(int value, string parameterName)
    {
        if (value < 0)
        {
            throw new ArgumentOutOfRangeException(parameterName, "Analysis counts must not be negative.");
        }
    }
}
