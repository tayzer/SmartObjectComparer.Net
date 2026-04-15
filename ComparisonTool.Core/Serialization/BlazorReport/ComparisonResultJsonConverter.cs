using System.Text.Json;
using System.Text.Json.Serialization;
using KellermanSoftware.CompareNetObjects;

namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// Custom JSON converter for <see cref="ComparisonResult"/> from CompareNetObjects.
/// Only serializes the Differences list — the only property used by Blazor UI components.
/// </summary>
public sealed class ComparisonResultJsonConverter : JsonConverter<ComparisonResult>
{
    public override ComparisonResult Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException("Expected StartObject token.");
        }

        var result = new ComparisonResult(new ComparisonConfig());

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return result;
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException("Expected PropertyName token.");
            }

            var propertyName = reader.GetString();
            reader.Read();

            switch (propertyName)
            {
                case "differences":
                    var differences = JsonSerializer.Deserialize<List<Difference>>(ref reader, options);
                    if (differences != null)
                    {
                        foreach (var diff in differences)
                        {
                            result.Differences.Add(diff);
                        }
                    }

                    break;
                default:
                    reader.Skip();
                    break;
            }
        }

        throw new JsonException("Unexpected end of JSON.");
    }

    public override void Write(Utf8JsonWriter writer, ComparisonResult value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("differences");
        JsonSerializer.Serialize(writer, value.Differences, options);
        writer.WriteEndObject();
    }
}
