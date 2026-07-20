using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportAffectedPairDifference
{
    public StaticReportAffectedPairDifference(
        string relativePath,
        string propertyPath,
        string normalizedPath,
        string category,
        int occurrenceCount,
        RequestPairOutcome outcome,
        int? statusCodeA = null,
        int? statusCodeB = null,
        string? valueA = null,
        string? valueB = null,
        string? message = null)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            throw new ArgumentException("Affected pair path must not be empty.", nameof(relativePath));
        }

        RelativePath = relativePath.Trim();
        PropertyPath = string.IsNullOrWhiteSpace(propertyPath) ? normalizedPath : propertyPath.Trim();
        NormalizedPath = string.IsNullOrWhiteSpace(normalizedPath) ? PropertyPath : normalizedPath.Trim();
        Category = string.IsNullOrWhiteSpace(category) ? "Value Differences" : category.Trim();
        OccurrenceCount = Math.Max(1, occurrenceCount);
        Outcome = outcome;
        StatusCodeA = statusCodeA;
        StatusCodeB = statusCodeB;
        ValueA = valueA;
        ValueB = valueB;
        Message = string.IsNullOrWhiteSpace(message) ? null : message;
    }

    public string RelativePath { get; }

    public string PropertyPath { get; }

    public string NormalizedPath { get; }

    public string Category { get; }

    public int OccurrenceCount { get; }

    public RequestPairOutcome Outcome { get; }

    public int? StatusCodeA { get; }

    public int? StatusCodeB { get; }

    public string? ValueA { get; }

    public string? ValueB { get; }

    public string? Message { get; }
}
