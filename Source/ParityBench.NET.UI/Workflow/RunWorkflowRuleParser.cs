using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.UI.Workflow;

public static class RunWorkflowRuleParser
{
    public static IReadOnlyList<IgnoreRuleDefinition> ParseIgnoreRules(string text) =>
        ReadLines(text)
            .Select(line => new IgnoreRuleDefinition(line.Value))
            .ToList();

    public static IReadOnlyList<SmartIgnoreRuleDefinition> ParseSmartIgnoreRules(string text)
    {
        List<SmartIgnoreRuleDefinition> rules = new List<SmartIgnoreRuleDefinition>();
        foreach (RuleLine line in ReadLines(text))
        {
            int separatorIndex = line.Value.IndexOf('=', StringComparison.Ordinal);
            if (separatorIndex <= 0 || separatorIndex == line.Value.Length - 1)
            {
                throw new InvalidOperationException($"Smart ignore rule on line {line.Number} must use Kind=Value format.");
            }

            string kindText = line.Value[..separatorIndex].Trim();
            string value = line.Value[(separatorIndex + 1)..].Trim();
            if (!Enum.TryParse(kindText, ignoreCase: true, out SmartIgnoreRuleKind kind))
            {
                throw new InvalidOperationException($"Smart ignore rule on line {line.Number} has unknown kind '{kindText}'.");
            }

            rules.Add(new SmartIgnoreRuleDefinition(kind, value));
        }

        return rules;
    }

    public static IReadOnlyList<MaskRuleDefinition> ParseMaskRules(string text)
    {
        List<MaskRuleDefinition> rules = new List<MaskRuleDefinition>();
        foreach (RuleLine line in ReadLines(text))
        {
            string[] segments = line.Value.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
            {
                continue;
            }

            string propertyPath = segments[0];
            int preserveLastCharacters = 0;
            string maskCharacter = "*";

            foreach (string segment in segments.Skip(1))
            {
                int separatorIndex = segment.IndexOf('=', StringComparison.Ordinal);
                if (separatorIndex <= 0 || separatorIndex == segment.Length - 1)
                {
                    throw new InvalidOperationException($"Mask rule option on line {line.Number} must use Name=Value format.");
                }

                string optionName = segment[..separatorIndex].Trim();
                string optionValue = segment[(separatorIndex + 1)..].Trim();
                if (string.Equals(optionName, "preserveLast", StringComparison.OrdinalIgnoreCase))
                {
                    if (!int.TryParse(optionValue, out preserveLastCharacters) || preserveLastCharacters < 0)
                    {
                        throw new InvalidOperationException($"Mask rule preserveLast on line {line.Number} must be a non-negative whole number.");
                    }

                    continue;
                }

                if (string.Equals(optionName, "mask", StringComparison.OrdinalIgnoreCase))
                {
                    maskCharacter = optionValue;
                    continue;
                }

                throw new InvalidOperationException($"Mask rule option on line {line.Number} has unknown name '{optionName}'.");
            }

            rules.Add(new MaskRuleDefinition(propertyPath, preserveLastCharacters, maskCharacter));
        }

        return rules;
    }

    private static IEnumerable<RuleLine> ReadLines(string text)
    {
        string source = text ?? string.Empty;
        string[] lines = source.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
        for (int index = 0; index < lines.Length; index++)
        {
            string line = lines[index].Trim();
            if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
            {
                continue;
            }

            yield return new RuleLine(index + 1, line);
        }
    }

    private sealed record RuleLine(int Number, string Value);
}
