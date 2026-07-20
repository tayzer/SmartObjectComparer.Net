namespace ParityBench.NET.Domain.Comparison;

public sealed record SmartIgnoreRuleDefinition
{
    public SmartIgnoreRuleDefinition(
        SmartIgnoreRuleKind kind,
        string value,
        bool isEnabled = true,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Smart ignore rule value must not be empty.", nameof(value));
        }

        Kind = kind;
        Value = value.Trim();
        IsEnabled = isEnabled;
        Description = string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public SmartIgnoreRuleKind Kind { get; }

    public string Value { get; }

    public bool IsEnabled { get; }

    public string? Description { get; }
}
