namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Categories of differences for better organization
/// </summary>
public enum DifferenceCategory
{
    NumericValueChanged,
    DateTimeChanged,
    BooleanValueChanged,
    CollectionItemChanged,
    ItemAdded,
    ItemRemoved,
    NullValueChange,
    ValueChanged,
    GeneralValueChanged,
    UncategorizedDifference,
    Other
}