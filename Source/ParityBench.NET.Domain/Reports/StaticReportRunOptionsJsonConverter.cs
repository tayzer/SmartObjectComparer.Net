using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

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
        string responseModelName = ReadOptionalString(root, "responseModelName") ?? ReadOptionalString(root, "modelName") ?? "Auto";
        ComparisonOptions? comparison = ReadOptional<ComparisonOptions>(root, "comparison", options);
        RequestExecutionOptions? requestExecution = ReadOptional<RequestExecutionOptions>(root, "requestExecution", options);
        ContractProfileSelection? contractProfile = ReadOptional<ContractProfileSelection>(root, "contractProfile", options) ?? ReadOptional<ContractProfileSelection>(root, "alternateContract", options);
        RetentionMode? runRetentionModeOverride = ReadOptional<RetentionMode>(root, "runRetentionModeOverride", options);
        string? comparisonRulesSnapshotHash = ReadOptionalString(root, "comparisonRulesSnapshotHash");

        return new RunOptions(
            requestBatch,
            endpointA,
            endpointB,
            timeout,
            maxConcurrency,
            responseModelName,
            comparison,
            requestExecution,
            contractProfile,
            runRetentionModeOverride: runRetentionModeOverride,
            comparisonRulesSnapshotHash: comparisonRulesSnapshotHash,
            baseline: ReadBaseline(root));
    }

    // BaselineBinding is built through factories that enforce which fields each mode
    // requires, so it is read field by field rather than deserialized structurally.
    private static BaselineBinding? ReadBaseline(JsonElement root)
    {
        if (!root.TryGetProperty("baseline", out JsonElement baseline) || baseline.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!baseline.TryGetProperty("mode", out JsonElement modeElement)
            || !Enum.TryParse(modeElement.GetString(), ignoreCase: true, out BaselineRunMode mode))
        {
            return null;
        }

        EndpointSlot slot = baseline.TryGetProperty("baselineSlot", out JsonElement slotElement)
            && Enum.TryParse(slotElement.GetString(), ignoreCase: true, out EndpointSlot parsedSlot)
                ? parsedSlot
                : EndpointSlot.A;

        switch (mode)
        {
            case BaselineRunMode.CaptureBaseline:
                string? captureName = ReadOptionalString(baseline, "captureName");
                return string.IsNullOrWhiteSpace(captureName) ? null : BaselineBinding.ForCapture(captureName, slot);

            case BaselineRunMode.BaselineVsLive:
                string? baselineId = ReadOptionalString(baseline, "baselineId");
                int? version = baseline.TryGetProperty("version", out JsonElement versionElement)
                    && versionElement.TryGetInt32(out int parsedVersion)
                        ? parsedVersion
                        : null;

                return string.IsNullOrWhiteSpace(baselineId) || version is not > 0
                    ? null
                    : BaselineBinding.ForReplay(new BaselineId(baselineId), version.Value, slot);

            default:
                return null;
        }
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
        writer.WriteString("responseModelName", value.ResponseModelName);
        writer.WriteString("modelName", value.ResponseModelName);
        writer.WritePropertyName("comparison");
        JsonSerializer.Serialize(writer, value.Comparison, options);
        writer.WritePropertyName("requestExecution");
        JsonSerializer.Serialize(writer, value.RequestExecution, options);
        writer.WritePropertyName("contractProfile");
        JsonSerializer.Serialize(writer, value.ContractProfile, options);
        writer.WritePropertyName("runRetentionModeOverride");
        JsonSerializer.Serialize(writer, value.RunRetentionModeOverride, options);
        writer.WriteString("comparisonRulesSnapshotHash", value.ComparisonRulesSnapshotHash);

        if (value.Baseline is { } baseline)
        {
            writer.WritePropertyName("baseline");
            writer.WriteStartObject();
            writer.WriteString("mode", baseline.Mode.ToString());
            writer.WriteString("baselineSlot", baseline.BaselineSlot.ToString());
            writer.WriteString("baselineId", baseline.BaselineId?.Value);
            if (baseline.Version is { } version)
            {
                writer.WriteNumber("version", version);
            }

            writer.WriteString("captureName", baseline.CaptureName);
            writer.WriteEndObject();
        }

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
