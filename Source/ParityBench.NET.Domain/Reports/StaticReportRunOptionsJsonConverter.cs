using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.AlternateContracts;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Reports;

internal sealed class StaticReportRunOptionsJsonConverter : JsonConverter<RunOptions>
{
    public override RunOptions Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        JsonElement root = document.RootElement;

        RequestBatchReference requestBatch = ReadRequired<RequestBatchReference>(root, "requestBatch", options);
        EndpointDefinition endpointA = ReadRequired<EndpointDefinition>(root, "endpointA", options);
        EndpointDefinition endpointB = ReadRequired<EndpointDefinition>(root, "endpointB", options);
        TimeSpan timeout = ReadRequired<TimeSpan>(root, "timeout", options);
        int maxConcurrency = ReadRequired<int>(root, "maxConcurrency", options);
        string modelName = ReadOptionalString(root, "modelName") ?? "Auto";
        ComparisonOptions? comparison = ReadOptional<ComparisonOptions>(root, "comparison", options);
        RequestExecutionOptions? requestExecution = ReadOptional<RequestExecutionOptions>(root, "requestExecution", options);
        AlternateContractOptions? alternateContract = ReadOptional<AlternateContractOptions>(root, "alternateContract", options);

        return new RunOptions(
            requestBatch,
            endpointA,
            endpointB,
            timeout,
            maxConcurrency,
            modelName,
            comparison,
            requestExecution,
            alternateContract);
    }

    public override void Write(
        Utf8JsonWriter writer,
        RunOptions value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WritePropertyName("requestBatch");
        JsonSerializer.Serialize(writer, value.RequestBatch, options);
        writer.WritePropertyName("endpointA");
        JsonSerializer.Serialize(writer, value.EndpointA, options);
        writer.WritePropertyName("endpointB");
        JsonSerializer.Serialize(writer, value.EndpointB, options);
        writer.WritePropertyName("timeout");
        JsonSerializer.Serialize(writer, value.Timeout, options);
        writer.WriteNumber("maxConcurrency", value.MaxConcurrency);
        writer.WriteString("modelName", value.ModelName);
        writer.WritePropertyName("comparison");
        JsonSerializer.Serialize(writer, value.Comparison, options);
        writer.WritePropertyName("requestExecution");
        JsonSerializer.Serialize(writer, value.RequestExecution, options);
        writer.WritePropertyName("alternateContract");
        JsonSerializer.Serialize(writer, value.AlternateContract, options);
        writer.WriteEndObject();
    }

    private static T ReadRequired<T>(
        JsonElement root,
        string propertyName,
        JsonSerializerOptions options)
    {
        if (!root.TryGetProperty(propertyName, out JsonElement value))
        {
            throw new JsonException($"Static report run options are missing '{propertyName}'.");
        }

        return value.Deserialize<T>(options) ?? throw new JsonException($"Static report run options property '{propertyName}' is null.");
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
}
