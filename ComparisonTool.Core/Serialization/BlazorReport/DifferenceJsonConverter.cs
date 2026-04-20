using System.Text.Json;
using System.Text.Json.Serialization;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// Custom JSON converter for <see cref="Difference"/> from CompareNetObjects.
/// Serializes only the displayable properties (PropertyName, Object1Value, Object2Value, etc.)
/// and skips live object references (Object1, Object2, ParentObject1, ParentObject2)
/// which are not serialization-safe.
/// </summary>
public sealed class DifferenceJsonConverter : JsonConverter<Difference>
{
    public override Difference Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        var diff = new Difference();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return diff;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "propertyName":
                    diff.PropertyName = reader.GetString() ?? string.Empty;
                    break;
                case "object1Value":
                    diff.Object1Value = ReadValue(ref reader)?.ToString();
                    break;
                case "object2Value":
                    diff.Object2Value = ReadValue(ref reader)?.ToString();
                    break;
                case "childPropertyName":
                    diff.ChildPropertyName = reader.GetString() ?? string.Empty;
                    break;
                case "parentPropertyName":
                    // ParentPropertyName is read-only on Difference; skip it
                    SafeSkip(ref reader);
                    break;
                case "messageShort":
                    // MessageShort is read-only on Difference; skip it on deserialization
                    SafeSkip(ref reader);
                    break;
                default:
                    SafeSkip(ref reader);
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON.");
    }

    private static void SafeSkip(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.StartObject:
                PerformSafeSkip(reader);
                break;
            case JsonTokenType.StartArray:
                PerformSafeSkip(reader);
                break;
            default:
                // For simple values, just skip
                break;
        }
    }

    private static void PerformSafeSkip(Utf8JsonReader reader)
    {
        int depth = 1;
        while (depth > 0 && reader.Read())
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject or JsonTokenType.StartArray:
                    depth++;
                    break;
                case JsonTokenType.EndObject or JsonTokenType.EndArray:
                    depth--;
                    break;
            }
        }
    }

    public override void Write(Utf8JsonWriter writer, Difference value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("propertyName", value.PropertyName ?? string.Empty);
        WriteValue(writer, "object1Value", value.Object1Value);
        WriteValue(writer, "object2Value", value.Object2Value);

        if (!string.IsNullOrEmpty(value.ChildPropertyName))
        {
            writer.WriteString("childPropertyName", value.ChildPropertyName);
        }

        if (!string.IsNullOrEmpty(value.ParentPropertyName))
        {
            writer.WriteString("parentPropertyName", value.ParentPropertyName);
        }

        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, string propertyName, object? value)
    {
        if (value == null)
        {
            writer.WriteNull(propertyName);
        }
        else
        {
            writer.WriteString(propertyName, value.ToString());
        }
    }

    private static object? ReadValue(ref Utf8JsonReader reader) =>
        reader.TokenType switch
        {
            JsonTokenType.Null => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l) ? l : reader.GetDouble(),
            JsonTokenType.True => true,
            JsonTokenType.False => false,
            _ => throw new JsonException($"Unexpected token type {reader.TokenType} for difference value."),
        };
}
