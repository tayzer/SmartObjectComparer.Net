namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportDifferenceIndex
{
    public StaticReportDifferenceIndex(
        int totalDifferences,
        int affectedPairCount,
        IReadOnlyList<StaticReportPropertyDifferenceSummary>? properties = null)
    {
        TotalDifferences = Math.Max(0, totalDifferences);
        AffectedPairCount = Math.Max(0, affectedPairCount);
        Properties = (properties ?? Array.Empty<StaticReportPropertyDifferenceSummary>()).ToList();
    }

    public int TotalDifferences { get; }

    public int AffectedPairCount { get; }

    public IReadOnlyList<StaticReportPropertyDifferenceSummary> Properties { get; }
}
