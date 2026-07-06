using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

internal sealed class StaticReportRequestPairResultJsonConverter : JsonConverter<RequestPairResult>
{
    public override RequestPairResult Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        string relativePath = ReadRequiredString(root, "relativePath");
        RequestPairOutcome outcome = ReadRequired<RequestPairOutcome>(root, "outcome", options);
        ResponseArtifactMetadata? responseA = ReadOptional<ResponseArtifactMetadata>(root, "responseA", options);
        ResponseArtifactMetadata? responseB = ReadOptional<ResponseArtifactMetadata>(root, "responseB", options);
        string? errorMessage = ReadOptionalString(root, "errorMessage");
        bool? areEqual = ReadOptional<bool>(root, "areEqual", options);
        int? differenceCount = ReadOptional<int>(root, "differenceCount", options);
        List<ComparisonDifference>? differences = ReadOptional<List<ComparisonDifference>>(root, "differences", options);
        string? outcomeMessage = ReadOptionalString(root, "outcomeMessage");
        List<StaticReportRawTextDifference>? rawTextDifferences = ReadOptional<List<StaticReportRawTextDifference>>(root, "rawTextDifferences", options);

        return new RequestPairResult(
            relativePath,
            outcome,
            responseA,
            responseB,
            errorMessage,
            areEqual,
            differenceCount,
            differences,
            outcomeMessage,
            rawTextDifferences);
    }

    public override void Write(
        Utf8JsonWriter writer,
        RequestPairResult value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("relativePath", value.RelativePath);
        writer.WritePropertyName("outcome");
        JsonSerializer.Serialize(writer, value.Outcome, options);
        WriteOptional(writer, "responseA", value.ResponseA, options);
        WriteOptional(writer, "responseB", value.ResponseB, options);
        WriteOptionalString(writer, "errorMessage", value.ErrorMessage);
        WriteOptional(writer, "areEqual", value.AreEqual, options);
        writer.WriteNumber("differenceCount", value.DifferenceCount);
        writer.WritePropertyName("differences");
        JsonSerializer.Serialize(writer, value.Differences, options);
        WriteOptionalString(writer, "outcomeMessage", value.OutcomeMessage);
        writer.WritePropertyName("rawTextDifferences");
        JsonSerializer.Serialize(writer, value.RawTextDifferences, options);
        writer.WriteEndObject();
    }

    private static string ReadRequiredString(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind != JsonValueKind.String)
        {
            throw new JsonException($"Static report pair is missing '{propertyName}'.");
        }

        return value.GetString() ?? throw new JsonException($"Static report pair property '{propertyName}' is null.");
    }

    private static T ReadRequired<T>(
        JsonElement root,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new JsonException($"Static report pair is missing '{propertyName}'.");
        }

        return value.Deserialize<T>(options) ?? throw new JsonException($"Static report pair property '{propertyName}' is null.");
    }

    private static T? ReadOptional<T>(
        JsonElement root,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return default;
        }

        return value.Deserialize<T>(options);
    }

    private static string? ReadOptionalString(
        JsonElement root,
        string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        return value.GetString();
    }

    private static void WriteOptional<T>(
        Utf8JsonWriter writer,
        string propertyName,
        T? value,
        JsonSerializerOptions options)
    {
        writer.WritePropertyName(propertyName);
        JsonSerializer.Serialize(writer, value, options);
    }

    private static void WriteOptionalString(
        Utf8JsonWriter writer,
        string propertyName,
        string? value)
    {
        if (value is null)
        {
            writer.WriteNull(propertyName);
            return;
        }

        writer.WriteString(propertyName, value);
    }
}
