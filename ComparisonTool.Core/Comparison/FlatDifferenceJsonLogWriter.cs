using System.Globalization;
using System.Reflection;
using System.Text.Json;
using ComparisonTool.Core.Comparison.Results;
using KellermanSoftware.CompareNetObjects;
using Microsoft.Extensions.Logging;

namespace ComparisonTool.Core.Comparison;

/// <summary>
/// Writes a flat JSON artifact containing one entry per raw comparison difference.
/// </summary>
public static class FlatDifferenceJsonLogWriter
{
    public const string MetadataKey = "FlatDifferenceJsonPath";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly PropertyInfo[] DifferenceProperties = typeof(Difference)
        .GetProperties(BindingFlags.Public | BindingFlags.Instance)
        .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
        .ToArray();

    public static void TryWrite(
        MultiFolderComparisonResult result,
        string runType,
        string? runId,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            var outputDirectory = Path.Combine(Directory.GetCurrentDirectory(), "Logs", "Comparisons");
            Directory.CreateDirectory(outputDirectory);

            var resolvedRunType = SanitizeFileComponent(runType);
            var resolvedRunId = ResolveRunId(runId);
            var outputPath = Path.Combine(outputDirectory, $"{resolvedRunType}_{resolvedRunId}_differences.json");

            using var stream = File.Create(outputPath);
            JsonSerializer.Serialize(stream, BuildEntries(result), JsonOptions);

            result.Metadata ??= new Dictionary<string, object>(StringComparer.Ordinal);
            result.Metadata[MetadataKey] = outputPath;

            logger?.LogInformation(
                "Wrote flat difference log to {OutputPath}",
                outputPath);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to write flat difference log for {RunType}", runType);
        }
    }

    private static List<Dictionary<string, object?>> BuildEntries(MultiFolderComparisonResult result)
    {
        var entries = new List<Dictionary<string, object?>>();

        for (var pairIndex = 0; pairIndex < result.FilePairResults.Count; pairIndex++)
        {
            var pairResult = result.FilePairResults[pairIndex];
            var differences = pairResult.Result?.Differences;
            var rawTextDifferences = pairResult.RawTextDifferences;

            if ((differences == null || differences.Count == 0) && (rawTextDifferences == null || rawTextDifferences.Count == 0))
            {
                continue;
            }

            for (var differenceIndex = 0; differenceIndex < (differences?.Count ?? 0); differenceIndex++)
            {
                var difference = differences![differenceIndex];
                if (difference == null)
                {
                    continue;
                }

                var entry = CreateBaseEntry(pairIndex, pairResult);
                entry["DifferenceSource"] = "Structured";
                entry["DifferenceIndex"] = differenceIndex;

                foreach (var property in DifferenceProperties)
                {
                    try
                    {
                        entry[property.Name] = NormalizeValue(property.GetValue(difference));
                    }
                    catch
                    {
                        entry[property.Name] = null;
                    }
                }

                entries.Add(entry);
            }

            for (var rawDifferenceIndex = 0; rawDifferenceIndex < (rawTextDifferences?.Count ?? 0); rawDifferenceIndex++)
            {
                var rawDifference = rawTextDifferences![rawDifferenceIndex];
                var entry = CreateBaseEntry(pairIndex, pairResult);
                entry["DifferenceSource"] = "RawText";
                entry["DifferenceIndex"] = rawDifferenceIndex;
                entry["Type"] = rawDifference.Type.ToString();
                entry["LineNumberA"] = rawDifference.LineNumberA;
                entry["LineNumberB"] = rawDifference.LineNumberB;
                entry["TextA"] = rawDifference.TextA;
                entry["TextB"] = rawDifference.TextB;
                entry["Description"] = rawDifference.Description;

                entries.Add(entry);
            }
        }

        return entries;
    }

    private static Dictionary<string, object?> CreateBaseEntry(int pairIndex, FilePairComparisonResult pairResult) =>
        new(StringComparer.Ordinal)
        {
            ["PairIndex"] = pairIndex,
            ["File1Name"] = pairResult.File1Name,
            ["File2Name"] = pairResult.File2Name,
            ["RequestRelativePath"] = pairResult.RequestRelativePath,
            ["PairOutcome"] = pairResult.PairOutcome?.ToString(),
        };

    private static object? NormalizeValue(object? value)
    {
        if (value == null)
        {
            return null;
        }

        var valueType = value.GetType();

        if (value is string || value is bool || value is Guid || value is DateTime || value is DateTimeOffset || value is TimeSpan)
        {
            return value;
        }

        if (valueType.IsEnum)
        {
            return value.ToString();
        }

        if (value is IFormattable formattable)
        {
            return formattable.ToString(null, CultureInfo.InvariantCulture);
        }

        return value.ToString();
    }

    private static string ResolveRunId(string? runId)
    {
        if (!string.IsNullOrWhiteSpace(runId))
        {
            return SanitizeFileComponent(runId);
        }

        return $"{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";
    }

    private static string SanitizeFileComponent(string value)
    {
        var invalidChars = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidChars.Contains(character) ? '_' : character).ToArray());
        return sanitized.Replace(' ', '_');
    }
}