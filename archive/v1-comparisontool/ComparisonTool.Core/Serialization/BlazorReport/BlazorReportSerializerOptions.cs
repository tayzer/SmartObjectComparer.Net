using System.Text.Json;
using System.Text.Json.Serialization;

namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// Provides JSON serializer options configured for round-tripping report data
/// between the CLI (serializer) and the Blazor WASM report (deserializer).
/// </summary>
public static class BlazorReportSerializerOptions
{
    public static readonly JsonSerializerOptions Default = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            WriteIndented = false,
            Converters =
            {
                new JsonStringEnumConverter(JsonNamingPolicy.CamelCase),
                new DifferenceJsonConverter(),
                new ComparisonResultJsonConverter(),
                new MetadataDictionaryJsonConverter(),
            },
        };
        return options;
    }
}
