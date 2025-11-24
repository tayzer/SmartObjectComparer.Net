// <copyright file="EnhancedDifferenceCategory.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace ComparisonTool.Core.Comparison.Analysis;

/// <summary>
/// Enhanced categories of differences for more detailed analysis.
/// </summary>
public enum EnhancedDifferenceCategory {
    // Basic categories from original DifferenceCategory
    TextContentChanged,
    NumericValueChanged,
    DateTimeChanged,
    BooleanValueChanged,
    CollectionItemChanged,
    ItemAdded,
    ItemRemoved,
    NullValueChange,

    // New enhanced categories
    CollectionElementMissing,      // A collection element is missing a specific property
    CollectionElementExtraProperty, // A collection element has an extra property not in the other
    CollectionElementCountMismatch, // Different number of elements in a collection
    CollectionElementOutOfOrder,   // Same elements but in different order

    MissingRequiredField,          // A field marked as required is missing
    InconsistentDataType,          // Field exists but with inconsistent data type
    XmlAttributeValueChanged,      // Value of an XML attribute changed
    XmlAttributeMissing,           // XML attribute missing

    IdentifierMismatch,            // IDs, GUIDs, or other identifiers don't match
    StatusValueChange,             // Changes to status fields
    TimestampChange,               // Changes to timestamp/date fields
    FormatMismatch,                // Data format differences (e.g., date formats)

    NameOrLabelChange,             // Changes to name, label, or description fields
    CalculatedValueChange,         // Changes in calculated or derived values
    ConfigurationChange,           // Changes in configuration or settings

    SchemaViolation,               // Changes that violate the expected schema
    StructuralMismatch,            // Significant structural differences

    Other,                          // Fallback category
}
