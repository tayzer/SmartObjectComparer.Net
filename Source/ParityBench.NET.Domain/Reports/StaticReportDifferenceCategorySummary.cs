namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportDifferenceCategorySummary
{
    public StaticReportDifferenceCategorySummary(
        string category,
        string displayName,
        int occurrenceCount,
        int affectedPairCount)
    {
        if (string.IsNullOrWhiteSpace(category))
        {
            throw new ArgumentException("Category must not be empty.", nameof(category));
        }

        if (occurrenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(occurrenceCount), "Occurrence count must not be negative.");
        }

        if (affectedPairCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(affectedPairCount), "Affected pair count must not be negative.");
        }

        Category = category.Trim();
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? Category : displayName.Trim();
        OccurrenceCount = occurrenceCount;
        AffectedPairCount = affectedPairCount;
    }

    public string Category { get; }

    public string DisplayName { get; }

    public int OccurrenceCount { get; }

    public int AffectedPairCount { get; }
}
