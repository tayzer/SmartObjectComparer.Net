namespace ParityBench.NET.Domain.Comparison;

public sealed record IgnoreRuleDefinition
{
    public IgnoreRuleDefinition(
        string propertyPath,
        bool ignoreCompletely = true,
        bool ignoreCollectionOrder = false,
        bool treatNullAndEmptyCollectionsAsEqual = false)
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("Ignore rule property path must not be empty.", nameof(propertyPath));
        }

        PropertyPath = propertyPath.Trim();
        IgnoreCompletely = ignoreCompletely;
        IgnoreCollectionOrder = ignoreCollectionOrder;
        TreatNullAndEmptyCollectionsAsEqual = treatNullAndEmptyCollectionsAsEqual;
    }

    public string PropertyPath { get; }

    public bool IgnoreCompletely { get; }

    public bool IgnoreCollectionOrder { get; }

    public bool TreatNullAndEmptyCollectionsAsEqual { get; }
}
