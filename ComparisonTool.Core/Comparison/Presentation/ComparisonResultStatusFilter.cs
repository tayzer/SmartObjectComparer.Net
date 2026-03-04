namespace ComparisonTool.Core.Comparison.Presentation;

/// <summary>
/// Defines status filtering options for comparison result grid items.
/// </summary>
public enum ComparisonResultStatusFilter
{
    /// <summary>Include all rows.</summary>
    All,

    /// <summary>Only rows marked equal.</summary>
    Equal,

    /// <summary>Only rows marked different without errors.</summary>
    Different,

    /// <summary>Only rows with comparison errors.</summary>
    Error,

    /// <summary>Only request pairs with status-code mismatch outcome.</summary>
    StatusMismatch,

    /// <summary>Only request pairs where one or both endpoints returned non-success/failure outcomes.</summary>
    NonSuccess,
}
