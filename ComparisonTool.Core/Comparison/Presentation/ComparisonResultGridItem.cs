using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.Comparison.Presentation;

/// <summary>
/// Represents a row in comparison result views (web and static reports).
/// </summary>
public sealed class ComparisonResultGridItem
{
    /// <summary>Gets or sets the original pair index in the source result list.</summary>
    public int OriginalIndex { get; set; }

    /// <summary>Gets or sets the first file name.</summary>
    public string File1Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the second file name.</summary>
    public string File2Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the request relative path when available.</summary>
    public string? RequestRelativePath { get; set; }

    /// <summary>Gets or sets a value indicating whether the pair is equal.</summary>
    public bool AreEqual { get; set; }

    /// <summary>Gets or sets a value indicating whether the pair has an error.</summary>
    public bool HasError { get; set; }

    /// <summary>Gets or sets the comparison error message.</summary>
    public string ErrorMessage { get; set; } = string.Empty;

    /// <summary>Gets or sets the difference count for this pair.</summary>
    public int DifferenceCount { get; set; }

    /// <summary>Gets or sets the category or pattern label.</summary>
    public string Category { get; set; } = string.Empty;

    /// <summary>Gets or sets endpoint A status code when available.</summary>
    public int? HttpStatusCodeA { get; set; }

    /// <summary>Gets or sets endpoint B status code when available.</summary>
    public int? HttpStatusCodeB { get; set; }

    /// <summary>Gets or sets the request-pair outcome when available.</summary>
    public RequestPairOutcome? PairOutcome { get; set; }
}
