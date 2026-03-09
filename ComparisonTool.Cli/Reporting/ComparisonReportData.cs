using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;

namespace ComparisonTool.Cli.Reporting;

internal sealed class ComparisonReportDto
{
    public int SchemaVersion { get; init; } = 1;

    public string ReportId { get; init; } = string.Empty;

    public DateTime GeneratedAt { get; init; }

    public string Command { get; init; } = string.Empty;

    public string? Model { get; init; }

    public string? Directory1 { get; init; }

    public string? Directory2 { get; init; }

    public string? EndpointA { get; init; }

    public string? EndpointB { get; init; }

    public string? JobId { get; init; }

    public double ElapsedSeconds { get; init; }

    public ComparisonSummaryDto Summary { get; init; } = new();

    public MostAffectedFieldsReportDto MostAffectedFields { get; init; } = new();

    public List<LabelCountDto> PairOutcomeCounts { get; init; } = new();

    public List<ComparisonFilePairDto> FilePairs { get; init; } = new();

    public Dictionary<string, object>? Metadata { get; init; }
}

internal sealed class ComparisonSummaryDto
{
    public int TotalPairs { get; init; }

    public bool AllEqual { get; init; }

    public int EqualCount { get; init; }

    public int DifferentCount { get; init; }

    public int ErrorCount { get; init; }
}

internal sealed class MostAffectedFieldsReportDto
{
    public int Top { get; init; }

    public int StructuredPairCount { get; init; }

    public int ExcludedRawTextPairCount { get; init; }

    public List<MostAffectedFieldReportDto> Fields { get; init; } = new();
}

internal sealed class MostAffectedFieldReportDto
{
    public string FieldPath { get; init; } = string.Empty;

    public int AffectedPairCount { get; init; }

    public int OccurrenceCount { get; init; }
}

internal sealed class ComparisonFilePairDto
{
    public string PairId { get; init; } = string.Empty;

    public int Index { get; init; }

    public string File1 { get; init; } = string.Empty;

    public string File2 { get; init; } = string.Empty;

    public string? File1Path { get; init; }

    public string? File2Path { get; init; }

    public string? RequestRelativePath { get; init; }

    public bool AreEqual { get; init; }

    public bool HasError { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorType { get; init; }

    public string? PairOutcome { get; init; }

    public int? HttpStatusA { get; init; }

    public int? HttpStatusB { get; init; }

    public int DifferenceCount { get; init; }

    public string ComparisonKind { get; init; } = string.Empty;

    public List<string> AffectedFields { get; init; } = new();

    public List<LabelCountDto> CategoryCounts { get; init; } = new();

    public List<LabelCountDto> RootObjectCounts { get; init; } = new();

    public List<StructuredDifferenceDto> Differences { get; init; } = new();

    public List<RawTextDifferenceDto> RawTextDifferences { get; init; } = new();
}

internal sealed class StructuredDifferenceDto
{
    public string PropertyName { get; init; } = string.Empty;

    public object? Expected { get; init; }

    public object? Actual { get; init; }
}

internal sealed class RawTextDifferenceDto
{
    public string Type { get; init; } = string.Empty;

    public int? LineNumberA { get; init; }

    public int? LineNumberB { get; init; }

    public string? TextA { get; init; }

    public string? TextB { get; init; }

    public string Description { get; init; } = string.Empty;
}

internal sealed class LabelCountDto
{
    public string Label { get; init; } = string.Empty;

    public int Count { get; init; }
}

internal static class ComparisonReportMapper
{
    private const int MostAffectedFieldsLimit = 15;

    public static ComparisonReportDto Map(ReportContext context)
    {
        var pairs = context.Result.FilePairResults;

        return new ComparisonReportDto
        {
            ReportId = BuildReportId(context),
            GeneratedAt = context.GeneratedAtUtc,
            Command = context.CommandName,
            Model = context.ModelName,
            Directory1 = context.Directory1,
            Directory2 = context.Directory2,
            EndpointA = context.EndpointA,
            EndpointB = context.EndpointB,
            JobId = context.JobId,
            ElapsedSeconds = Math.Round(context.Elapsed.TotalSeconds, 2),
            Summary = new ComparisonSummaryDto
            {
                TotalPairs = context.Result.TotalPairsCompared,
                AllEqual = context.Result.AllEqual,
                EqualCount = pairs.Count(pair => pair.AreEqual),
                DifferentCount = pairs.Count(pair => !pair.AreEqual && !pair.HasError),
                ErrorCount = pairs.Count(pair => pair.HasError),
            },
            MostAffectedFields = new MostAffectedFieldsReportDto
            {
                Top = MostAffectedFieldsLimit,
                StructuredPairCount = context.MostAffectedFields.StructuredPairCount,
                ExcludedRawTextPairCount = context.MostAffectedFields.ExcludedRawTextPairCount,
                Fields = context.MostAffectedFields.Fields
                    .Take(MostAffectedFieldsLimit)
                    .Select(field => new MostAffectedFieldReportDto
                    {
                        FieldPath = field.FieldPath,
                        AffectedPairCount = field.AffectedPairCount,
                        OccurrenceCount = field.OccurrenceCount,
                    })
                    .ToList(),
            },
            PairOutcomeCounts = pairs
                .Where(pair => pair.PairOutcome != null)
                .GroupBy(pair => pair.PairOutcome!.Value.ToString(), StringComparer.Ordinal)
                .Select(group => new LabelCountDto
                {
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToList(),
            FilePairs = pairs
                .Select((pair, index) => MapPair(pair, index))
                .ToList(),
            Metadata = context.Result.Metadata.Count > 0
                ? context.Result.Metadata.ToDictionary(kvp => kvp.Key, kvp => kvp.Value, StringComparer.Ordinal)
                : null,
        };
    }

    private static ComparisonFilePairDto MapPair(FilePairComparisonResult pair, int index)
    {
        var differences = pair.Result?.Differences?
            .Select(diff => new StructuredDifferenceDto
            {
                PropertyName = diff.PropertyName ?? string.Empty,
                Expected = diff.Object1Value,
                Actual = diff.Object2Value,
            })
            .ToList() ?? new List<StructuredDifferenceDto>();

        var rawTextDifferences = pair.RawTextDifferences?
            .Select(diff => new RawTextDifferenceDto
            {
                Type = diff.Type.ToString(),
                LineNumberA = diff.LineNumberA,
                LineNumberB = diff.LineNumberB,
                TextA = diff.TextA,
                TextB = diff.TextB,
                Description = diff.Description,
            })
            .ToList() ?? new List<RawTextDifferenceDto>();

        return new ComparisonFilePairDto
        {
            PairId = BuildPairId(pair, index),
            Index = index + 1,
            File1 = pair.File1Name,
            File2 = pair.File2Name,
            File1Path = pair.File1Path,
            File2Path = pair.File2Path,
            RequestRelativePath = pair.RequestRelativePath,
            AreEqual = pair.AreEqual,
            HasError = pair.HasError,
            ErrorMessage = pair.ErrorMessage,
            ErrorType = pair.ErrorType,
            PairOutcome = pair.PairOutcome?.ToString(),
            HttpStatusA = pair.HttpStatusCodeA,
            HttpStatusB = pair.HttpStatusCodeB,
            DifferenceCount = differences.Count > 0 ? differences.Count : rawTextDifferences.Count,
            ComparisonKind = GetComparisonKind(pair, differences.Count, rawTextDifferences.Count),
            AffectedFields = differences
                .Select(diff => diff.PropertyName)
                .Where(propertyName => !string.IsNullOrWhiteSpace(propertyName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(propertyName => propertyName, StringComparer.Ordinal)
                .ToList(),
            CategoryCounts = pair.Summary?.DifferencesByChangeType
                .OrderByDescending(category => category.Value.Count)
                .Select(category => new LabelCountDto
                {
                    Label = category.Key.ToString(),
                    Count = category.Value.Count,
                })
                .ToList() ?? new List<LabelCountDto>(),
            RootObjectCounts = pair.Summary?.DifferencesByRootObject
                .OrderByDescending(rootObject => rootObject.Value.Count)
                .Select(rootObject => new LabelCountDto
                {
                    Label = rootObject.Key,
                    Count = rootObject.Value.Count,
                })
                .ToList() ?? new List<LabelCountDto>(),
            Differences = differences,
            RawTextDifferences = rawTextDifferences,
        };
    }

    private static string BuildReportId(ReportContext context)
    {
        var identity = string.Join(
            "|",
            context.CommandName,
            context.ModelName ?? string.Empty,
            Path.GetFileName(context.Directory1 ?? string.Empty),
            Path.GetFileName(context.Directory2 ?? string.Empty),
            context.EndpointA ?? string.Empty,
            context.EndpointB ?? string.Empty,
            context.JobId ?? string.Empty,
            context.GeneratedAtUtc.ToString("O"));

        return $"{context.CommandName}-{CreateStableHash(identity)}";
    }

    private static string BuildPairId(FilePairComparisonResult pair, int index)
    {
        var stableSegment = string.Join(
            "|",
            index + 1,
            pair.RequestRelativePath ?? string.Empty,
            pair.File1Name ?? string.Empty,
            Path.GetFileName(pair.File1Path ?? string.Empty),
            pair.File2Name ?? string.Empty,
            Path.GetFileName(pair.File2Path ?? string.Empty));

        return $"pair-{index + 1}-{CreateStableHash(stableSegment)}";
    }

    private static string GetComparisonKind(FilePairComparisonResult pair, int differenceCount, int rawDifferenceCount)
    {
        if (pair.HasError)
        {
            return "error";
        }

        if (rawDifferenceCount > 0)
        {
            return "raw-text";
        }

        if (differenceCount > 0)
        {
            return "structured";
        }

        return "equal";
    }

    private static string CreateStableHash(string value)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(hash.AsSpan(0, 8)).ToLowerInvariant();
    }
}

internal static class ComparisonReportJson
{
    public static readonly JsonSerializerOptions CompactOptions = CreateOptions(writeIndented: false);

    public static readonly JsonSerializerOptions IndentedOptions = CreateOptions(writeIndented: true);

    private static JsonSerializerOptions CreateOptions(bool writeIndented)
    {
        return new JsonSerializerOptions
        {
            WriteIndented = writeIndented,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };
    }
}