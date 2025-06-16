namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Categories of differences for better organization
/// </summary>
public enum DifferenceCategory
{
    TextContentChanged,
    NumericValueChanged,
    DateTimeChanged,
    BooleanValueChanged,
    CollectionItemChanged,
    ItemAdded,
    ItemRemoved,
    NullValueChange,
    ValueChanged,
    GeneralValueChanged,
    Other
}