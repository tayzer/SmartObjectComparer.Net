namespace ParityBench.NET.Infrastructure;

public sealed class ComparisonRuleDefaultsFileOptions
{
    public string? IgnoreRulesFile { get; set; }

    public bool IgnoreXmlNamespacesWhenFileMissing { get; set; } = true;

    public bool? IgnoreXmlNamespacesOverride { get; set; }
}
