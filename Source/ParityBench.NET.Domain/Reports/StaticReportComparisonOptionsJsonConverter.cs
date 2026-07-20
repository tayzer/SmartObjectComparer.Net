using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Domain.Reports;

internal sealed class StaticReportComparisonOptionsJsonConverter : JsonConverter<ComparisonOptions>
{
    public override ComparisonOptions Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        return new ComparisonOptions(
            ReadOptional(root, "ignoreCollectionOrder", defaultValue: false),
            ReadOptional(root, "ignoreStringCase", defaultValue: false),
            ReadOptional(root, "ignoreTrailingWhitespaceAtEnd", defaultValue: false),
            ReadOptional(root, "treatNullAndEmptyCollectionsAsEqual", defaultValue: false),
            ReadOptional(root, "ignoreXmlNamespaces", defaultValue: true),
            ReadOptional(root, "maxDifferences", defaultValue: 100),
            ReadOptionalList<IgnoreRuleDefinition>(root, "ignoreRules", options),
            ReadOptionalList<SmartIgnoreRuleDefinition>(root, "smartIgnoreRules", options),
            ReadOptionalList<MaskRuleDefinition>(root, "maskRules", options));
    }

    public override void Write(
        Utf8JsonWriter writer,
        ComparisonOptions value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteBoolean("ignoreCollectionOrder", value.IgnoreCollectionOrder);
        writer.WriteBoolean("ignoreStringCase", value.IgnoreStringCase);
        writer.WriteBoolean("ignoreTrailingWhitespaceAtEnd", value.IgnoreTrailingWhitespaceAtEnd);
        writer.WriteBoolean("treatNullAndEmptyCollectionsAsEqual", value.TreatNullAndEmptyCollectionsAsEqual);
        writer.WriteBoolean("ignoreXmlNamespaces", value.IgnoreXmlNamespaces);
        writer.WriteNumber("maxDifferences", value.MaxDifferences);
        writer.WritePropertyName("ignoreRules");
        JsonSerializer.Serialize(writer, value.IgnoreRules, options);
        writer.WritePropertyName("smartIgnoreRules");
        JsonSerializer.Serialize(writer, value.SmartIgnoreRules, options);
        writer.WritePropertyName("maskRules");
        JsonSerializer.Serialize(writer, value.MaskRules, options);
        writer.WriteEndObject();
    }

    private static bool ReadOptional(
        JsonElement root,
        string propertyName,
        bool defaultValue) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.GetBoolean()
            : defaultValue;

    private static int ReadOptional(
        JsonElement root,
        string propertyName,
        int defaultValue) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind != JsonValueKind.Null
            ? value.GetInt32()
            : defaultValue;

    private static IReadOnlyList<T>? ReadOptionalList<T>(
        JsonElement root,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.Deserialize<List<T>>(options);
    }
}
