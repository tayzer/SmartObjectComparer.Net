namespace ParityBench.NET.Domain.Comparison;

public sealed record ComparisonOptions
{
    public ComparisonOptions(
        bool ignoreCollectionOrder = false,
        bool ignoreStringCase = false,
        bool ignoreTrailingWhitespaceAtEnd = false,
        bool treatNullAndEmptyCollectionsAsEqual = false,
        bool ignoreXmlNamespaces = true,
        int maxDifferences = 100,
        IEnumerable<IgnoreRuleDefinition>? ignoreRules = null,
        IEnumerable<SmartIgnoreRuleDefinition>? smartIgnoreRules = null,
        IEnumerable<MaskRuleDefinition>? maskRules = null,
        bool includeAllDifferences = false)
    {
        if (maxDifferences <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDifferences), "Maximum differences must be greater than zero.");
        }

        IgnoreCollectionOrder = ignoreCollectionOrder;
        IgnoreStringCase = ignoreStringCase;
        IgnoreTrailingWhitespaceAtEnd = ignoreTrailingWhitespaceAtEnd;
        TreatNullAndEmptyCollectionsAsEqual = treatNullAndEmptyCollectionsAsEqual;
        IgnoreXmlNamespaces = ignoreXmlNamespaces;
        MaxDifferences = maxDifferences;
        IgnoreRules = (ignoreRules ?? Array.Empty<IgnoreRuleDefinition>()).ToList();
        SmartIgnoreRules = (smartIgnoreRules ?? Array.Empty<SmartIgnoreRuleDefinition>()).ToList();
        MaskRules = (maskRules ?? Array.Empty<MaskRuleDefinition>()).ToList();
        IncludeAllDifferences = includeAllDifferences;
    }

    public bool IgnoreCollectionOrder { get; }

    public bool IgnoreStringCase { get; }

    public bool IgnoreTrailingWhitespaceAtEnd { get; }

    public bool TreatNullAndEmptyCollectionsAsEqual { get; }

    public bool IgnoreXmlNamespaces { get; }

    public int MaxDifferences { get; }

    public IReadOnlyList<IgnoreRuleDefinition> IgnoreRules { get; }

    public IReadOnlyList<SmartIgnoreRuleDefinition> SmartIgnoreRules { get; }

    public IReadOnlyList<MaskRuleDefinition> MaskRules { get; }

    public bool IncludeAllDifferences { get; }

    public bool HasComparisonAffectingOptions =>
        IgnoreCollectionOrder ||
        IgnoreStringCase ||
        IgnoreTrailingWhitespaceAtEnd ||
        TreatNullAndEmptyCollectionsAsEqual ||
        IgnoreRules.Count > 0 ||
        SmartIgnoreRules.Count > 0 ||
        MaskRules.Count > 0;
}
