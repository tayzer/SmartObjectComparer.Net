namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// On-disk JSON contract for accepted-difference persistence.
/// </summary>
public sealed class AcceptedDifferenceProfileStore
{
    /// <summary>
    /// Gets or sets the storage schema version.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>
    /// Gets or sets persisted profiles.
    /// </summary>
    public List<AcceptedDifferenceProfile> Profiles { get; set; } = new();
}