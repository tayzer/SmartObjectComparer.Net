namespace ParityBench.NET.Domain.Comparison;

public sealed record ComparisonRuleDefaults
{
    public ComparisonRuleDefaults(
        bool ignoreCollectionOrder = false,
        bool ignoreStringCase = false,
        bool ignoreTrailingWhitespaceAtEnd = false,
        bool treatNullAndEmptyCollectionsAsEqual = false,
        bool ignoreXmlNamespaces = false,
        IEnumerable<IgnoreRuleDefinition>? ignoreRules = null,
        IEnumerable<SmartIgnoreRuleDefinition>? smartIgnoreRules = null,
        IEnumerable<MaskRuleDefinition>? maskRules = null)
    {
        IgnoreCollectionOrder = ignoreCollectionOrder;
        IgnoreStringCase = ignoreStringCase;
        IgnoreTrailingWhitespaceAtEnd = ignoreTrailingWhitespaceAtEnd;
        TreatNullAndEmptyCollectionsAsEqual = treatNullAndEmptyCollectionsAsEqual;
        IgnoreXmlNamespaces = ignoreXmlNamespaces;
        IgnoreRules = (ignoreRules ?? Array.Empty<IgnoreRuleDefinition>()).ToList();
        SmartIgnoreRules = (smartIgnoreRules ?? Array.Empty<SmartIgnoreRuleDefinition>()).ToList();
        MaskRules = (maskRules ?? Array.Empty<MaskRuleDefinition>()).ToList();
    }

    public bool IgnoreCollectionOrder { get; }

    public bool IgnoreStringCase { get; }

    public bool IgnoreTrailingWhitespaceAtEnd { get; }

    public bool TreatNullAndEmptyCollectionsAsEqual { get; }

    public bool IgnoreXmlNamespaces { get; }

    public IReadOnlyList<IgnoreRuleDefinition> IgnoreRules { get; }

    public IReadOnlyList<SmartIgnoreRuleDefinition> SmartIgnoreRules { get; }

    public IReadOnlyList<MaskRuleDefinition> MaskRules { get; }

    public bool HasDefaults =>
        IgnoreCollectionOrder ||
        IgnoreStringCase ||
        IgnoreTrailingWhitespaceAtEnd ||
        TreatNullAndEmptyCollectionsAsEqual ||
        IgnoreXmlNamespaces ||
        IgnoreRules.Count > 0 ||
        SmartIgnoreRules.Count > 0 ||
        MaskRules.Count > 0;
}
