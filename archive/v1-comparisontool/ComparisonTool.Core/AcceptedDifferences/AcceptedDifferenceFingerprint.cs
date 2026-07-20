using ComparisonTool.Core.Comparison.Analysis;

namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// Stable signature used to match repeated differences across runs.
/// </summary>
public sealed class AcceptedDifferenceFingerprint
{
    /// <summary>
    /// Gets or sets the hashed fingerprint identifier.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized property path.
    /// </summary>
    public string NormalizedPropertyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the derived difference category.
    /// </summary>
    public DifferenceCategory Category { get; set; }

    /// <summary>
    /// Gets or sets the scrubbed expected-side value pattern.
    /// </summary>
    public string ExpectedValuePattern { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the scrubbed actual-side value pattern.
    /// </summary>
    public string ActualValuePattern { get; set; } = string.Empty;
}