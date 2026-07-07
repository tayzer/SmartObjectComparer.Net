namespace ParityBench.NET.Domain.AcceptedDifferences;

public sealed record AcceptedDifferenceProfileStore
{
    public int SchemaVersion { get; init; } = 1;

    public List<AcceptedDifferenceProfile> Profiles { get; init; } = new List<AcceptedDifferenceProfile>();
}
