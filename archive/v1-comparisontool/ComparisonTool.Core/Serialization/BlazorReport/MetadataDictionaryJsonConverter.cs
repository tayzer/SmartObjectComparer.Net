using System.Text.Json;
using System.Text.Json.Serialization;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// Custom JSON converter for the Metadata dictionary (Dictionary&lt;string, object&gt;)
/// that stores typed analysis results as boxed values.
/// Serializes each value with a type discriminator wrapper so it can be deserialized
/// back to the correct concrete type.
/// </summary>
public sealed class MetadataDictionaryJsonConverter : JsonConverter<Dictionary<string, object>>
{
    private const string TypeDiscriminator = "$type";
    private const string ValueProperty = "$value";

    private static readonly Dictionary<string, Type> KnownTypes = new(StringComparer.Ordinal)
    {
        ["ComparisonPhaseTimings"] = typeof(ComparisonPhaseTimings),
        ["SemanticDifferenceAnalysis"] = typeof(SemanticDifferenceAnalysis),
        ["ExecutionOutcomeSummary"] = typeof(ExecutionOutcomeSummary),
        ["RequestComparisonRunTimings"] = typeof(RequestComparisonRunTimings),
    };

    private static readonly Dictionary<Type, string> KnownTypeNames =
        KnownTypes.ToDictionary(kvp => kvp.Value, kvp => kvp.Key);

    public override Dictionary<string, object> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject for metadata dictionary.");
        }

        var dict = new Dictionary<string, object>(StringComparer.Ordinal);

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return dict;
            }

            var key = reader.GetString()!;
            reader.Read();

            if (reader.TokenType == JsonTokenType.StartObject)
            {
                var value = ReadTypedValue(ref reader, options);
                if (value != null)
                {
                    dict[key] = value;
                }
            }
            else
            {
                reader.Skip();
            }
        }

        throw new JsonException("Unexpected end of metadata dictionary.");
    }

    public override void Write(Utf8JsonWriter writer, Dictionary<string, object> value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var kvp in value)
        {
            writer.WritePropertyName(kvp.Key);

            if (kvp.Value == null)
            {
                writer.WriteNullValue();
                continue;
            }

            var valueType = kvp.Value.GetType();

            if (KnownTypeNames.TryGetValue(valueType, out var typeName))
            {
                writer.WriteStartObject();
                writer.WriteString(TypeDiscriminator, typeName);
                writer.WritePropertyName(ValueProperty);
                JsonSerializer.Serialize(writer, kvp.Value, valueType, options);
                writer.WriteEndObject();
            }
            else
            {
                // Unknown type — serialize with type name for potential future handling
                writer.WriteStartObject();
                writer.WriteString(TypeDiscriminator, valueType.Name);
                writer.WritePropertyName(ValueProperty);
                JsonSerializer.Serialize(writer, kvp.Value, valueType, options);
                writer.WriteEndObject();
            }
        }

        writer.WriteEndObject();
    }

    private static object? ReadTypedValue(ref Utf8JsonReader reader, JsonSerializerOptions options)
    {
        // We're inside a StartObject. Parse the whole object to find $type and $value.
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;

        string? typeName = null;
        if (root.TryGetProperty(TypeDiscriminator, out var typeEl))
        {
            typeName = typeEl.GetString();
        }

        if (typeName != null && KnownTypes.TryGetValue(typeName, out var targetType))
        {
            if (root.TryGetProperty(ValueProperty, out var valueEl))
            {
                return JsonSerializer.Deserialize(valueEl.GetRawText(), targetType, options);
            }
        }

        // Unknown type — skip
        return null;
    }
}
