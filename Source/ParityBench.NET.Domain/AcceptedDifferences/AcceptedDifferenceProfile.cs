namespace ParityBench.NET.Domain.AcceptedDifferences;

public sealed record AcceptedDifferenceProfile
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string Fingerprint { get; init; } = string.Empty;

    public string NormalizedPropertyPath { get; init; } = string.Empty;

    public string Category { get; init; } = string.Empty;

    public string ValueAPattern { get; init; } = string.Empty;

    public string ValueBPattern { get; init; } = string.Empty;

    public string SamplePropertyPath { get; init; } = string.Empty;

    public string SampleValueA { get; init; } = string.Empty;

    public string SampleValueB { get; init; } = string.Empty;

    public AcceptedDifferenceStatus Status { get; init; }

    public string? TicketId { get; init; }

    public string? Notes { get; init; }

    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}
