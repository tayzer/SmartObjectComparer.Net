namespace ParityBench.NET.Domain.Comparison;

public sealed record ComparisonDifference
{
    public ComparisonDifference(
        string propertyPath,
        string? valueA,
        string? valueB,
        string? message = null)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("Difference property path must not be empty.", nameof(propertyPath));
        }

        PropertyPath = propertyPath.Trim();
        ValueA = valueA;
        ValueB = valueB;
        Message = string.IsNullOrWhiteSpace(message) ? null : message;
    }

    public string PropertyPath { get; }

    public string? ValueA { get; }

    public string? ValueB { get; }

    public string? Message { get; }
}
