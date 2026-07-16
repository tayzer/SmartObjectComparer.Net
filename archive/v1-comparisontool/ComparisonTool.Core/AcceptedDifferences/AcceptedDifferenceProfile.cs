using ComparisonTool.Core.Comparison.Analysis;

namespace ComparisonTool.Core.AcceptedDifferences;

/// <summary>
/// Persisted tester decision for a difference fingerprint.
/// </summary>
public sealed class AcceptedDifferenceProfile
{
    /// <summary>
    /// Gets or sets the profile identifier.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Gets or sets the stable fingerprint.
    /// </summary>
    public string Fingerprint { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the normalized property path captured when the profile was created.
    /// </summary>
    public string NormalizedPropertyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the category captured when the profile was created.
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

    /// <summary>
    /// Gets or sets the sample source property path.
    /// </summary>
    public string SamplePropertyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a sample expected value for UI context.
    /// </summary>
    public string SampleExpectedValue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a sample actual value for UI context.
    /// </summary>
    public string SampleActualValue { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the persisted status.
    /// </summary>
    public AcceptedDifferenceStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the linked ticket identifier for known bugs.
    /// </summary>
    public string? TicketId { get; set; }

    /// <summary>
    /// Gets or sets free-form tester notes.
    /// </summary>
    public string? Notes { get; set; }

    /// <summary>
    /// Gets or sets the creation timestamp in UTC.
    /// </summary>
    public DateTime CreatedUtc { get; set; }

    /// <summary>
    /// Gets or sets the last update timestamp in UTC.
    /// </summary>
    public DateTime UpdatedUtc { get; set; }
}