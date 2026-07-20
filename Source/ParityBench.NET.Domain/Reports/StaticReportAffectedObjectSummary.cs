namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportAffectedObjectSummary
{
    public StaticReportAffectedObjectSummary(
        string identifier,
        int differenceCount,
        string category,
        string? outcome = null)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier must not be empty.", nameof(identifier));
        }

        if (differenceCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(differenceCount), "Difference count must not be negative.");
        }

        Identifier = identifier.Trim();
        DifferenceCount = differenceCount;
        Category = string.IsNullOrWhiteSpace(category) ? "Other" : category.Trim();
        Outcome = string.IsNullOrWhiteSpace(outcome) ? null : outcome.Trim();
    }

    public string Identifier { get; }

    public int DifferenceCount { get; }

    public string Category { get; }

    public string? Outcome { get; }
}
