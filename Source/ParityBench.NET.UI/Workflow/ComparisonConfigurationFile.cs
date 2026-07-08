using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.UI.Workflow;

public sealed record ComparisonConfigurationFile(
    int SchemaVersion,
    ComparisonConfigurationGlobalSettings GlobalSettings,
    int MaxDifferences,
    IReadOnlyList<IgnoreRuleDefinition> IgnoreRules);

public sealed record ComparisonConfigurationGlobalSettings(
    bool IgnoreCollectionOrder,
    bool IgnoreStringCase,
    bool IgnoreTrailingWhitespaceAtEnd,
    bool TreatNullAndEmptyCollectionsAsEqual,
    bool IgnoreXmlNamespaces);

public static class ComparisonConfigurationFileSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string Serialize(ComparisonConfigurationFile configuration) =>
        JsonSerializer.Serialize(configuration, JsonOptions);

    public static ComparisonConfigurationFile Deserialize(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidOperationException("Comparison configuration must be a JSON object.");
        }

        ComparisonConfigurationFile? v2 = JsonSerializer.Deserialize<ComparisonConfigurationFile>(json, JsonOptions);
        if (v2 is not null && v2.GlobalSettings is not null && v2.IgnoreRules is not null)
        {
            return v2 with
            {
                SchemaVersion = v2.SchemaVersion == 0 ? 1 : v2.SchemaVersion,
                MaxDifferences = v2.MaxDifferences <= 0 ? 100 : v2.MaxDifferences,
                IgnoreRules = NormalizeIgnoreRules(v2.IgnoreRules),
            };
        }

        V1ComparisonConfigurationFile? v1 = JsonSerializer.Deserialize<V1ComparisonConfigurationFile>(json, JsonOptions);
        if (v1?.GlobalSettings is null)
        {
            throw new InvalidOperationException("File does not contain a supported comparison configuration.");
        }

        return new ComparisonConfigurationFile(
            v1.SchemaVersion == 0 ? 1 : v1.SchemaVersion,
            new ComparisonConfigurationGlobalSettings(
                v1.GlobalSettings.IgnoreCollectionOrder,
                v1.GlobalSettings.IgnoreStringCase,
                v1.GlobalSettings.IgnoreTrailingWhitespaceAtEnd,
                v1.GlobalSettings.TreatNullAndEmptyCollectionsAsEqual,
                v1.GlobalSettings.IgnoreXmlNamespaces),
            100,
            NormalizeIgnoreRules(v1.IgnoreRules ?? Array.Empty<IgnoreRuleDefinition>()));
    }

    private static IReadOnlyList<IgnoreRuleDefinition> NormalizeIgnoreRules(IEnumerable<IgnoreRuleDefinition> rules) =>
        rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .GroupBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed record V1ComparisonConfigurationFile(
        int SchemaVersion,
        V1ComparisonGlobalSettings? GlobalSettings,
        IReadOnlyList<IgnoreRuleDefinition>? IgnoreRules);

    private sealed record V1ComparisonGlobalSettings(
        bool IgnoreCollectionOrder,
        bool IgnoreStringCase,
        bool IgnoreTrailingWhitespaceAtEnd,
        bool TreatNullAndEmptyCollectionsAsEqual,
        bool IgnoreXmlNamespaces);
}

public static class MaskRuleFileSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    public static string Serialize(IReadOnlyList<MaskRuleDefinition> rules) =>
        JsonSerializer.Serialize(new MaskRuleFile(rules), JsonOptions);

    public static IReadOnlyList<MaskRuleDefinition> Deserialize(string json)
    {
        using JsonDocument document = JsonDocument.Parse(json);
        List<MaskRuleDefinition>? rules = document.RootElement.ValueKind == JsonValueKind.Array
            ? JsonSerializer.Deserialize<List<MaskRuleDefinition>>(json, JsonOptions)
            : JsonSerializer.Deserialize<MaskRuleFile>(json, JsonOptions)?.MaskRules?.ToList();

        return (rules ?? new List<MaskRuleDefinition>())
            .Where(rule => !string.IsNullOrWhiteSpace(rule.PropertyPath))
            .GroupBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .OrderBy(rule => rule.PropertyPath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private sealed record MaskRuleFile(IReadOnlyList<MaskRuleDefinition> MaskRules);
}
