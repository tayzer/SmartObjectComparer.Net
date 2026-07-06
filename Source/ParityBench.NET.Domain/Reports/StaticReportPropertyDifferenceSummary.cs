namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportPropertyDifferenceSummary
{
    public StaticReportPropertyDifferenceSummary(
        string propertyPath,
        string normalizedPath,
        string category,
        int occurrenceCount,
        int affectedPairCount,
        IReadOnlyList<StaticReportAffectedPairDifference>? affectedPairs = null)
    {
        if (string.IsNullOrWhiteSpace(normalizedPath))
        {
            throw new ArgumentException("Normalized property path must not be empty.", nameof(normalizedPath));
        }

        PropertyPath = string.IsNullOrWhiteSpace(propertyPath) ? normalizedPath.Trim() : propertyPath.Trim();
        NormalizedPath = normalizedPath.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? "Value Differences" : category.Trim();
        OccurrenceCount = Math.Max(0, occurrenceCount);
        AffectedPairCount = Math.Max(0, affectedPairCount);
        AffectedPairs = (affectedPairs ?? Array.Empty<StaticReportAffectedPairDifference>()).ToList();
    }

    public string PropertyPath { get; }

    public string NormalizedPath { get; }

    public string Category { get; }

    public int OccurrenceCount { get; }

    public int AffectedPairCount { get; }

    public IReadOnlyList<StaticReportAffectedPairDifference> AffectedPairs { get; }
}
