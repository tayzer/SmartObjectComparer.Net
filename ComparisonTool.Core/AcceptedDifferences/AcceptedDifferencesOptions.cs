namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// Configuration for accepted-difference persistence.
/// </summary>
public sealed class AcceptedDifferencesOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the feature is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the JSON store path. Relative paths are resolved from the app base directory.
    /// </summary>
    public string StorePath { get; set; } = "Data/accepted-differences.json";
}