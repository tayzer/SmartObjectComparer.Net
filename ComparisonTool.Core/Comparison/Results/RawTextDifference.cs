namespace ComparisonTool.Core.Comparison.Results;

/// <summary>
/// Represents the type of a raw text difference.
/// </summary>
public enum RawTextDifferenceType
{
    /// <summary>Line exists only in source A.</summary>
    OnlyInA,

    /// <summary>Line exists only in source B.</summary>
    OnlyInB,

    /// <summary>Line was modified between A and B.</summary>
    Modified,

    /// <summary>HTTP status code difference between A and B.</summary>
    StatusCodeDifference,
}

/// <summary>
/// Represents a single difference found during raw text comparison of non-success HTTP responses.
/// </summary>
public class RawTextDifference
{
    /// <summary>Gets or sets the type of difference.</summary>
    public RawTextDifferenceType Type { get; set; }

    /// <summary>Gets or sets the 1-based line number in source A (null if only in B).</summary>
    public int? LineNumberA { get; set; }

    /// <summary>Gets or sets the 1-based line number in source B (null if only in A).</summary>
    public int? LineNumberB { get; set; }

    /// <summary>Gets or sets the text content from source A.</summary>
    public string? TextA { get; set; }

    /// <summary>Gets or sets the text content from source B.</summary>
    public string? TextB { get; set; }

    /// <summary>Gets or sets a summary description of this difference.</summary>
    public string Description { get; set; } = string.Empty;
}
