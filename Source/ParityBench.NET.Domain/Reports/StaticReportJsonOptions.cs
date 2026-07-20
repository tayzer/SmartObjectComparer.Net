using System.Text.Json;
using System.Text.Json.Serialization;

namespace ParityBench.NET.Domain.Reports;

public static class StaticReportJsonOptions
{
    public static JsonSerializerOptions Create()
    {
        JsonSerializerOptions options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true,
        };

        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new StaticReportRequestPairResultJsonConverter());
        options.Converters.Add(new StaticReportRunOptionsJsonConverter());
        options.Converters.Add(new StaticReportComparisonOptionsJsonConverter());
        return options;
    }
}
