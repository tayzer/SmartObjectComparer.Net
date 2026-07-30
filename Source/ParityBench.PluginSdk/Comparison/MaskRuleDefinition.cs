namespace ParityBench.NET.Domain.Comparison;

public sealed record MaskRuleDefinition
{
    public MaskRuleDefinition(
        string propertyPath,
        int preserveLastCharacters = 0,
        string maskCharacter = "*")
    {
        if (string.IsNullOrWhiteSpace(propertyPath))
        {
            throw new ArgumentException("Mask rule property path must not be empty.", nameof(propertyPath));
        }

        if (preserveLastCharacters < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(preserveLastCharacters), "Preserved character count must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(maskCharacter) || maskCharacter.Length != 1)
        {
            throw new ArgumentException("Mask character must be exactly one character.", nameof(maskCharacter));
        }

        PropertyPath = propertyPath.Trim();
        PreserveLastCharacters = preserveLastCharacters;
        MaskCharacter = maskCharacter;
    }

    public string PropertyPath { get; }

    public int PreserveLastCharacters { get; }

    public string MaskCharacter { get; }
}
