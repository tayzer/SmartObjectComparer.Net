using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.RequestComparison.Models;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Comparison.Results;

public class FilePairComparisonResult
{
    public string File1Name { get; set; } = string.Empty;

    public string File2Name { get; set; } = string.Empty;

    public ComparisonResult? Result
    {
        get; set;
    }

    public DifferenceSummary? Summary
    {
        get; set;
    }

    /// <summary>
    /// Gets or sets the HTTP status code from endpoint A (request comparison only).
    /// </summary>
    public int? HttpStatusCodeA { get; set; }

    /// <summary>
    /// Gets or sets the HTTP status code from endpoint B (request comparison only).
    /// </summary>
    public int? HttpStatusCodeB { get; set; }

    /// <summary>
    /// Gets or sets the classified pair outcome for request comparison results.
    /// Null for non-request-comparison (file/folder) results.
    /// </summary>
    public RequestPairOutcome? PairOutcome { get; set; }

    /// <summary>
    /// Gets or sets the raw text differences when domain-model comparison was not applicable
    /// (e.g. non-success HTTP responses).
    /// </summary>
    public List<RawTextDifference>? RawTextDifferences { get; set; }

    /// <summary>
    /// Gets or sets error information if the comparison failed.
    /// When HasError is true, the files could not be compared (e.g., FileNotFound, deserialization errors).
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the exception type name if an error occurred.
    /// </summary>
    public string? ErrorType { get; set; }

    /// <summary>
    /// Gets a value indicating whether this file pair had an error during comparison.
    /// Files with errors should NOT be counted as "Equal" - they are comparison failures.
    /// </summary>
    public bool HasError => !string.IsNullOrEmpty(this.ErrorMessage);

    /// <summary>
    /// Gets a value indicating whether the files are equal.
    /// Returns false if there was an error, as we cannot determine equality without a successful comparison.
    /// </summary>
    public bool AreEqual => !this.HasError && (this.Summary?.AreEqual ?? false);
}
