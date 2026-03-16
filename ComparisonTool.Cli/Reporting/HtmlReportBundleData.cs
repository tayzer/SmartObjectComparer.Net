using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using ComparisonTool.Core.Comparison.Analysis;
using ComparisonTool.Core.Comparison.Results;
using DiffPlex;
using DiffPlex.DiffBuilder;
using DiffPlex.DiffBuilder.Model;

namespace ComparisonTool.Cli.Reporting;

internal sealed class HtmlReportBootstrapDto
{
    public int SchemaVersion { get; init; } = 2;

    public string Mode { get; init; } = string.Empty;

    public HtmlReportHeaderDto Report { get; init; } = new();

    public int DefaultPageSize { get; init; }

    public int DetailChunkSize { get; init; }

    public string? IndexPath { get; init; }

    public HtmlReportIndexDto? Index { get; init; }

    public List<HtmlReportDetailChunkDto>? DetailChunks { get; init; }
}

internal sealed class HtmlReportHeaderDto
{
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

    public List<string> MetadataKeys { get; init; } = new();
}

internal sealed class HtmlReportIndexDto
{
    public int TotalPairs { get; init; }

    public List<HtmlReportPairSummaryDto> Pairs { get; init; } = new();

    public List<HtmlReportPatternClusterDto> Patterns { get; init; } = new();

    public List<LabelCountDto> StatusCounts { get; init; } = new();

    public List<LabelCountDto> ComparisonKindCounts { get; init; } = new();

    public List<LabelCountDto> TopFields { get; init; } = new();
}

internal sealed class HtmlReportPairSummaryDto
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

    public List<string> PatternKeys { get; set; } = new();

    public string SearchText { get; init; } = string.Empty;

    public string DetailChunkId { get; set; } = string.Empty;

    public string? DetailChunkPath { get; set; }
}

internal sealed class HtmlReportPatternClusterDto
{
    public string Key { get; init; } = string.Empty;

    public string Label { get; init; } = string.Empty;

    public string Kind { get; init; } = string.Empty;

    public int Count { get; init; }

    public string? Description { get; init; }

    public List<string> SamplePairIds { get; init; } = new();
}

internal sealed class HtmlReportDetailChunkDto
{
    public string ChunkId { get; init; } = string.Empty;

    public List<string> PairIds { get; init; } = new();

    public List<HtmlReportPairDetailDto> Pairs { get; init; } = new();
}

internal sealed class HtmlReportPairDetailDto
{
    public string PairId { get; init; } = string.Empty;

    public int Index { get; init; }

    public bool AreEqual { get; init; }

    public bool HasError { get; init; }

    public string? ErrorMessage { get; init; }

    public string? ErrorType { get; init; }

    public List<StructuredDifferenceDto> Differences { get; init; } = new();

    public List<RawTextDifferenceDto> RawTextDifferences { get; init; } = new();

    public HtmlDiffDocumentDto? DiffDocument { get; init; }
}

internal sealed class HtmlDiffDocumentDto
{
    public string Format { get; init; } = string.Empty;

    public string LeftLabel { get; init; } = string.Empty;

    public string RightLabel { get; init; } = string.Empty;

    public int TotalLines { get; init; }

    public int ChangedLineCount { get; init; }

    public List<HtmlDiffLineDto> Lines { get; init; } = new();
}

internal sealed class HtmlDiffLineDto
{
    public int RowNumber { get; init; }

    public string ChangeType { get; init; } = string.Empty;

    public int? LeftLineNumber { get; init; }

    public int? RightLineNumber { get; init; }

    public string? LeftText { get; init; }

    public string? RightText { get; init; }
}

internal sealed class HtmlReportBundle
{
    public HtmlReportBootstrapDto Bootstrap { get; init; } = new();

    public List<HtmlReportBundleFileDto> Files { get; init; } = new();
}

internal sealed class HtmlReportBundleFileDto
{
    public string RelativePath { get; init; } = string.Empty;

    public object Payload { get; init; } = new();
}

internal static class HtmlReportBundleBuilder
{
    private const int MostAffectedFieldsLimit = 15;
    private const int PatternLimit = 36;
    private const int TopFieldLimit = 24;

    public static HtmlReportBundle BuildSingleFile(ReportContext context)
    {
        return Build(context, dataRootPath: null, inlineDetails: true);
    }

    public static HtmlReportBundle BuildStaticSite(ReportContext context, string dataRootPath)
    {
        return Build(context, dataRootPath, inlineDetails: false);
    }

    public static async Task<HtmlReportBootstrapDto> WriteStaticSiteAsync(
        ReportContext context,
        string dataRootPath,
        string outputDirectory)
    {
        var pairs = context.Result.FilePairResults;
        var reportId = ComparisonReportIdentity.BuildReportId(context);
        var pairOutcomeCounts = BuildPairOutcomeCounts(pairs);
        var summary = BuildSummary(context, pairs);
        var mostAffectedFields = BuildMostAffectedFields(context);
        var semanticAnalysis = TryGetMetadata<SemanticDifferenceAnalysis>(context.Result.Metadata, "SemanticAnalysis");
        var tagDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var workingPairs = new List<WorkingSummaryDto>(pairs.Count);
        var chunkSize = Math.Clamp(context.HtmlDetailChunkSize, 25, 1000);
        var chunkPairs = new List<HtmlReportPairDetailDto>(chunkSize);
        var chunkPairIds = new List<string>(chunkSize);
        var chunkNumber = 1;

        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            var pairId = ComparisonReportIdentity.BuildPairId(pair, index);
            var structuredDifferences = BuildStructuredDifferences(pair);
            var rawTextDifferences = BuildRawTextDifferences(pair);
            var affectedFields = structuredDifferences
                .Select(diff => diff.PropertyName)
                .Where(propertyName => !string.IsNullOrWhiteSpace(propertyName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(propertyName => propertyName, StringComparer.Ordinal)
                .ToList();
            var comparisonKind = GetComparisonKind(pair, structuredDifferences.Count, rawTextDifferences.Count);
            var patternKeys = BuildPatternKeys(pair, affectedFields, semanticAnalysis, tagDescriptions);
            var chunkId = $"pairs-{chunkNumber:0000}";
            var chunkPath = BuildChunkPath(dataRootPath, chunkId);

            var summaryDto = new HtmlReportPairSummaryDto
            {
                PairId = pairId,
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
                DifferenceCount = structuredDifferences.Count > 0 ? structuredDifferences.Count : rawTextDifferences.Count,
                ComparisonKind = comparisonKind,
                AffectedFields = affectedFields,
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
                SearchText = BuildSearchText(pair, affectedFields, comparisonKind),
                DetailChunkId = chunkId,
                DetailChunkPath = chunkPath,
            };

            workingPairs.Add(new WorkingSummaryDto
            {
                Summary = summaryDto,
                PatternKeys = patternKeys,
            });

            chunkPairIds.Add(pairId);
            chunkPairs.Add(new HtmlReportPairDetailDto
            {
                PairId = pairId,
                Index = index + 1,
                AreEqual = pair.AreEqual,
                HasError = pair.HasError,
                ErrorMessage = pair.ErrorMessage,
                ErrorType = pair.ErrorType,
                Differences = structuredDifferences,
                RawTextDifferences = rawTextDifferences,
                DiffDocument = BuildDiffDocument(pair, structuredDifferences),
            });

            if (chunkPairs.Count == chunkSize || index == pairs.Count - 1)
            {
                await WritePayloadAsync(
                    outputDirectory,
                    chunkPath,
                    new HtmlReportDetailChunkDto
                    {
                        ChunkId = chunkId,
                        PairIds = chunkPairIds,
                        Pairs = chunkPairs,
                    });

                chunkNumber++;
                chunkPairIds = new List<string>(chunkSize);
                chunkPairs = new List<HtmlReportPairDetailDto>(chunkSize);
            }
        }

        var recurringTagCounts = workingPairs
            .SelectMany(pair => pair.PatternKeys)
            .GroupBy(tag => tag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var workingPair in workingPairs)
        {
            workingPair.Summary.PatternKeys = workingPair.PatternKeys
                .Where(key => recurringTagCounts.GetValueOrDefault(key, 0) > 1)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
        }

        var patternClusters = recurringTagCounts
            .Where(entry => entry.Value > 1)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(PatternLimit)
            .Select(entry => new HtmlReportPatternClusterDto
            {
                Key = entry.Key,
                Label = HumanizePatternKey(entry.Key),
                Kind = GetPatternKind(entry.Key),
                Count = entry.Value,
                Description = tagDescriptions.GetValueOrDefault(entry.Key),
                SamplePairIds = workingPairs
                    .Where(pair => pair.Summary.PatternKeys.Contains(entry.Key, StringComparer.Ordinal))
                    .Take(6)
                    .Select(pair => pair.Summary.PairId)
                    .ToList(),
            })
            .ToList();

        var reportIndex = new HtmlReportIndexDto
        {
            TotalPairs = context.Result.TotalPairsCompared,
            Pairs = workingPairs.Select(pair => pair.Summary).ToList(),
            Patterns = patternClusters,
            StatusCounts = BuildStatusCounts(workingPairs.Select(pair => pair.Summary)),
            ComparisonKindCounts = workingPairs
                .Select(pair => pair.Summary)
                .GroupBy(pair => pair.ComparisonKind, StringComparer.Ordinal)
                .Select(group => new LabelCountDto
                {
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToList(),
            TopFields = workingPairs
                .SelectMany(pair => pair.Summary.AffectedFields)
                .GroupBy(field => field, StringComparer.Ordinal)
                .Select(group => new LabelCountDto
                {
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .Take(TopFieldLimit)
                .ToList(),
        };

        await WritePayloadAsync(outputDirectory, BuildIndexPath(dataRootPath), reportIndex);

        return new HtmlReportBootstrapDto
        {
            Mode = "static-site",
            Report = new HtmlReportHeaderDto
            {
                ReportId = reportId,
                GeneratedAt = context.GeneratedAtUtc,
                Command = context.CommandName,
                Model = context.ModelName,
                Directory1 = context.Directory1,
                Directory2 = context.Directory2,
                EndpointA = context.EndpointA,
                EndpointB = context.EndpointB,
                JobId = context.JobId,
                ElapsedSeconds = Math.Round(context.Elapsed.TotalSeconds, 2),
                Summary = summary,
                MostAffectedFields = mostAffectedFields,
                PairOutcomeCounts = pairOutcomeCounts,
                MetadataKeys = context.Result.Metadata.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList(),
            },
            DefaultPageSize = Math.Max(25, context.HtmlDefaultPageSize),
            DetailChunkSize = chunkSize,
            IndexPath = BuildIndexPath(dataRootPath),
        };
    }

    private static HtmlReportBundle Build(ReportContext context, string? dataRootPath, bool inlineDetails)
    {
        var pairs = context.Result.FilePairResults;
        var reportId = ComparisonReportIdentity.BuildReportId(context);
        var pairOutcomeCounts = BuildPairOutcomeCounts(pairs);
        var summary = BuildSummary(context, pairs);
        var mostAffectedFields = BuildMostAffectedFields(context);
        var semanticAnalysis = TryGetMetadata<SemanticDifferenceAnalysis>(context.Result.Metadata, "SemanticAnalysis");
        var tagDescriptions = new Dictionary<string, string>(StringComparer.Ordinal);
        var workingPairs = new List<WorkingPairDto>(pairs.Count);

        for (var index = 0; index < pairs.Count; index++)
        {
            var pair = pairs[index];
            var pairId = ComparisonReportIdentity.BuildPairId(pair, index);
            var structuredDifferences = BuildStructuredDifferences(pair);
            var rawTextDifferences = BuildRawTextDifferences(pair);
            var affectedFields = structuredDifferences
                .Select(diff => diff.PropertyName)
                .Where(propertyName => !string.IsNullOrWhiteSpace(propertyName))
                .Distinct(StringComparer.Ordinal)
                .OrderBy(propertyName => propertyName, StringComparer.Ordinal)
                .ToList();
            var comparisonKind = GetComparisonKind(pair, structuredDifferences.Count, rawTextDifferences.Count);
            var patternKeys = BuildPatternKeys(pair, affectedFields, semanticAnalysis, tagDescriptions);
            var summaryDto = new HtmlReportPairSummaryDto
            {
                PairId = pairId,
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
                DifferenceCount = structuredDifferences.Count > 0 ? structuredDifferences.Count : rawTextDifferences.Count,
                ComparisonKind = comparisonKind,
                AffectedFields = affectedFields,
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
                SearchText = BuildSearchText(pair, affectedFields, comparisonKind),
            };
            var detailDto = new HtmlReportPairDetailDto
            {
                PairId = pairId,
                Index = index + 1,
                AreEqual = pair.AreEqual,
                HasError = pair.HasError,
                ErrorMessage = pair.ErrorMessage,
                ErrorType = pair.ErrorType,
                Differences = structuredDifferences,
                RawTextDifferences = rawTextDifferences,
                DiffDocument = BuildDiffDocument(pair, structuredDifferences),
            };

            workingPairs.Add(new WorkingPairDto
            {
                Summary = summaryDto,
                Detail = detailDto,
                PatternKeys = patternKeys,
            });
        }

        var recurringTagCounts = workingPairs
            .SelectMany(pair => pair.PatternKeys)
            .GroupBy(tag => tag, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);

        foreach (var workingPair in workingPairs)
        {
            workingPair.Summary.PatternKeys = workingPair.PatternKeys
                .Where(key => recurringTagCounts.GetValueOrDefault(key, 0) > 1)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(key => key, StringComparer.Ordinal)
                .ToList();
        }

        var patternClusters = recurringTagCounts
            .Where(entry => entry.Value > 1)
            .OrderByDescending(entry => entry.Value)
            .ThenBy(entry => entry.Key, StringComparer.Ordinal)
            .Take(PatternLimit)
            .Select(entry => new HtmlReportPatternClusterDto
            {
                Key = entry.Key,
                Label = HumanizePatternKey(entry.Key),
                Kind = GetPatternKind(entry.Key),
                Count = entry.Value,
                Description = tagDescriptions.GetValueOrDefault(entry.Key),
                SamplePairIds = workingPairs
                    .Where(pair => pair.Summary.PatternKeys.Contains(entry.Key, StringComparer.Ordinal))
                    .Take(6)
                    .Select(pair => pair.Summary.PairId)
                    .ToList(),
            })
            .ToList();

        var chunkSize = Math.Clamp(context.HtmlDetailChunkSize, 25, 1000);
        var detailChunks = new List<HtmlReportDetailChunkDto>();
        var files = new List<HtmlReportBundleFileDto>();

        for (var chunkIndex = 0; chunkIndex < workingPairs.Count; chunkIndex += chunkSize)
        {
            var chunkPairs = workingPairs.Skip(chunkIndex).Take(chunkSize).ToList();
            var chunkId = $"pairs-{(chunkIndex / chunkSize) + 1:0000}";
            var chunkPath = inlineDetails ? null : BuildChunkPath(dataRootPath!, chunkId);

            foreach (var chunkPair in chunkPairs)
            {
                chunkPair.Summary.DetailChunkId = chunkId;
                chunkPair.Summary.DetailChunkPath = chunkPath;
            }

            var chunkDto = new HtmlReportDetailChunkDto
            {
                ChunkId = chunkId,
                PairIds = chunkPairs.Select(pair => pair.Summary.PairId).ToList(),
                Pairs = chunkPairs.Select(pair => pair.Detail).ToList(),
            };

            detailChunks.Add(chunkDto);

            if (!inlineDetails)
            {
                files.Add(new HtmlReportBundleFileDto
                {
                    RelativePath = chunkPath!,
                    Payload = chunkDto,
                });
            }
        }

        var reportIndex = new HtmlReportIndexDto
        {
            TotalPairs = context.Result.TotalPairsCompared,
            Pairs = workingPairs.Select(pair => pair.Summary).ToList(),
            Patterns = patternClusters,
            StatusCounts = BuildStatusCounts(workingPairs.Select(pair => pair.Summary)),
            ComparisonKindCounts = workingPairs
                .Select(pair => pair.Summary)
                .GroupBy(pair => pair.ComparisonKind, StringComparer.Ordinal)
                .Select(group => new LabelCountDto
                {
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .ToList(),
            TopFields = workingPairs
                .SelectMany(pair => pair.Summary.AffectedFields)
                .GroupBy(field => field, StringComparer.Ordinal)
                .Select(group => new LabelCountDto
                {
                    Label = group.Key,
                    Count = group.Count(),
                })
                .OrderByDescending(item => item.Count)
                .ThenBy(item => item.Label, StringComparer.Ordinal)
                .Take(TopFieldLimit)
                .ToList(),
        };

        if (!inlineDetails)
        {
            files.Add(new HtmlReportBundleFileDto
            {
                RelativePath = BuildIndexPath(dataRootPath!),
                Payload = reportIndex,
            });
        }

        return new HtmlReportBundle
        {
            Bootstrap = new HtmlReportBootstrapDto
            {
                Mode = inlineDetails ? "single-file" : "static-site",
                Report = new HtmlReportHeaderDto
                {
                    ReportId = reportId,
                    GeneratedAt = context.GeneratedAtUtc,
                    Command = context.CommandName,
                    Model = context.ModelName,
                    Directory1 = context.Directory1,
                    Directory2 = context.Directory2,
                    EndpointA = context.EndpointA,
                    EndpointB = context.EndpointB,
                    JobId = context.JobId,
                    ElapsedSeconds = Math.Round(context.Elapsed.TotalSeconds, 2),
                    Summary = summary,
                    MostAffectedFields = mostAffectedFields,
                    PairOutcomeCounts = pairOutcomeCounts,
                    MetadataKeys = context.Result.Metadata.Keys.OrderBy(key => key, StringComparer.Ordinal).ToList(),
                },
                DefaultPageSize = Math.Max(25, context.HtmlDefaultPageSize),
                DetailChunkSize = chunkSize,
                IndexPath = inlineDetails ? null : BuildIndexPath(dataRootPath!),
                Index = inlineDetails ? reportIndex : null,
                DetailChunks = inlineDetails ? detailChunks : null,
            },
            Files = files,
        };
    }

    private static ComparisonSummaryDto BuildSummary(ReportContext context, IReadOnlyList<FilePairComparisonResult> pairs)
    {
        return new ComparisonSummaryDto
        {
            TotalPairs = context.Result.TotalPairsCompared,
            AllEqual = context.Result.AllEqual,
            EqualCount = pairs.Count(pair => pair.AreEqual),
            DifferentCount = pairs.Count(pair => !pair.AreEqual && !pair.HasError),
            ErrorCount = pairs.Count(pair => pair.HasError),
        };
    }

    private static MostAffectedFieldsReportDto BuildMostAffectedFields(ReportContext context)
    {
        return new MostAffectedFieldsReportDto
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
        };
    }

    private static List<LabelCountDto> BuildPairOutcomeCounts(IReadOnlyList<FilePairComparisonResult> pairs)
    {
        return pairs
            .Where(pair => pair.PairOutcome != null)
            .GroupBy(pair => pair.PairOutcome!.Value.ToString(), StringComparer.Ordinal)
            .Select(group => new LabelCountDto
            {
                Label = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static List<StructuredDifferenceDto> BuildStructuredDifferences(FilePairComparisonResult pair)
    {
        return pair.Result?.Differences?
            .Select(diff => new StructuredDifferenceDto
            {
                PropertyName = diff.PropertyName ?? string.Empty,
                Expected = diff.Object1Value,
                Actual = diff.Object2Value,
            })
            .ToList() ?? new List<StructuredDifferenceDto>();
    }

    private static List<RawTextDifferenceDto> BuildRawTextDifferences(FilePairComparisonResult pair)
    {
        return pair.RawTextDifferences?
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
    }

    private static HtmlDiffDocumentDto? BuildDiffDocument(
        FilePairComparisonResult pair,
        IReadOnlyCollection<StructuredDifferenceDto> structuredDifferences)
    {
        if (pair.AreEqual && !pair.HasError)
        {
            return null;
        }

        var leftText = TryReadText(pair.File1Path);
        var rightText = TryReadText(pair.File2Path);

        if (string.IsNullOrWhiteSpace(leftText) && string.IsNullOrWhiteSpace(rightText) && structuredDifferences.Count > 0)
        {
            leftText = BuildStructuredFallbackText(structuredDifferences, useExpected: true);
            rightText = BuildStructuredFallbackText(structuredDifferences, useExpected: false);
        }

        if (string.IsNullOrWhiteSpace(leftText) && string.IsNullOrWhiteSpace(rightText))
        {
            return null;
        }

        var normalized = NormalizeDocuments(leftText, rightText);
        var builder = new SideBySideDiffBuilder(new Differ());
        var diff = builder.BuildDiffModel(normalized.LeftText ?? string.Empty, normalized.RightText ?? string.Empty);
        var totalLines = Math.Max(diff.OldText.Lines.Count, diff.NewText.Lines.Count);
        var lines = new List<HtmlDiffLineDto>(totalLines);
        var changedLineCount = 0;

        for (var rowIndex = 0; rowIndex < totalLines; rowIndex++)
        {
            var oldLine = rowIndex < diff.OldText.Lines.Count ? diff.OldText.Lines[rowIndex] : null;
            var newLine = rowIndex < diff.NewText.Lines.Count ? diff.NewText.Lines[rowIndex] : null;
            var changeType = ResolveChangeType(oldLine?.Type, newLine?.Type);
            if (!string.Equals(changeType, "unchanged", StringComparison.Ordinal))
            {
                changedLineCount++;
            }

            lines.Add(new HtmlDiffLineDto
            {
                RowNumber = rowIndex + 1,
                ChangeType = changeType,
                LeftLineNumber = GetLineNumber(oldLine),
                RightLineNumber = GetLineNumber(newLine),
                LeftText = GetLineText(oldLine),
                RightText = GetLineText(newLine),
            });
        }

        return new HtmlDiffDocumentDto
        {
            Format = normalized.Format,
            LeftLabel = Path.GetFileName(pair.File1Path ?? pair.File1Name) ?? pair.File1Name,
            RightLabel = Path.GetFileName(pair.File2Path ?? pair.File2Name) ?? pair.File2Name,
            TotalLines = lines.Count,
            ChangedLineCount = changedLineCount,
            Lines = lines,
        };
    }

    private static string? TryReadText(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static NormalizedDocumentPairDto NormalizeDocuments(string? leftText, string? rightText)
    {
        if (TryNormalizeXml(leftText, rightText, out var xmlLeft, out var xmlRight))
        {
            return new NormalizedDocumentPairDto
            {
                Format = "xml",
                LeftText = NormalizeLineEndings(xmlLeft),
                RightText = NormalizeLineEndings(xmlRight),
            };
        }

        if (TryNormalizeJson(leftText, rightText, out var jsonLeft, out var jsonRight))
        {
            return new NormalizedDocumentPairDto
            {
                Format = "json",
                LeftText = NormalizeLineEndings(jsonLeft),
                RightText = NormalizeLineEndings(jsonRight),
            };
        }

        return new NormalizedDocumentPairDto
        {
            Format = "text",
            LeftText = NormalizeLineEndings(leftText),
            RightText = NormalizeLineEndings(rightText),
        };
    }

    private static bool TryNormalizeXml(string? leftText, string? rightText, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;

        if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText))
        {
            return false;
        }

        if (!TryFormatXml(leftText, out left) || !TryFormatXml(rightText, out right))
        {
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryNormalizeJson(string? leftText, string? rightText, out string left, out string right)
    {
        left = string.Empty;
        right = string.Empty;

        if (string.IsNullOrWhiteSpace(leftText) || string.IsNullOrWhiteSpace(rightText))
        {
            return false;
        }

        if (!TryFormatJson(leftText, out left) || !TryFormatJson(rightText, out right))
        {
            left = string.Empty;
            right = string.Empty;
            return false;
        }

        return true;
    }

    private static bool TryFormatXml(string value, out string formatted)
    {
        formatted = string.Empty;

        try
        {
            var document = XDocument.Parse(value, LoadOptions.PreserveWhitespace);
            var settings = new XmlWriterSettings
            {
                Indent = true,
                OmitXmlDeclaration = document.Declaration == null,
                NewLineChars = "\n",
                NewLineHandling = NewLineHandling.Replace,
            };

            using var writer = new StringWriter();
            using var xmlWriter = XmlWriter.Create(writer, settings);
            document.Save(xmlWriter);
            xmlWriter.Flush();
            formatted = writer.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryFormatJson(string value, out string formatted)
    {
        formatted = string.Empty;

        try
        {
            using var document = JsonDocument.Parse(value);
            formatted = JsonSerializer.Serialize(document.RootElement, ComparisonReportJson.IndentedOptions);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string BuildStructuredFallbackText(
        IReadOnlyCollection<StructuredDifferenceDto> differences,
        bool useExpected)
    {
        return string.Join(
            Environment.NewLine,
            differences.Select(diff => $"{diff.PropertyName}: {FormatValue(useExpected ? diff.Expected : diff.Actual)}"));
    }

    private static string FormatValue(object? value)
    {
        if (value == null)
        {
            return "null";
        }

        if (value is string text)
        {
            return text;
        }

        if (value is bool or int or long or short or byte or decimal or double or float)
        {
            return Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        try
        {
            return JsonSerializer.Serialize(value, ComparisonReportJson.IndentedOptions);
        }
        catch
        {
            return value.ToString() ?? string.Empty;
        }
    }

    private static string BuildSearchText(
        FilePairComparisonResult pair,
        IReadOnlyCollection<string> affectedFields,
        string comparisonKind)
    {
        return string.Join(
                ' ',
                new[]
                {
                    pair.File1Name,
                    pair.File2Name,
                    pair.RequestRelativePath,
                    pair.PairOutcome?.ToString(),
                    pair.ErrorMessage,
                    comparisonKind,
                }
                .Concat(affectedFields)
                .Where(value => !string.IsNullOrWhiteSpace(value)))
            .ToLowerInvariant();
    }

    private static List<string> BuildPatternKeys(
        FilePairComparisonResult pair,
        IReadOnlyCollection<string> affectedFields,
        SemanticDifferenceAnalysis? semanticAnalysis,
        IDictionary<string, string> tagDescriptions)
    {
        var tags = new HashSet<string>(StringComparer.Ordinal)
        {
            $"kind:{GetComparisonKind(pair, pair.Result?.Differences?.Count ?? 0, pair.RawTextDifferences?.Count ?? 0)}",
        };

        if (pair.PairOutcome != null)
        {
            tags.Add($"outcome:{pair.PairOutcome.Value}");
        }

        if (!string.IsNullOrWhiteSpace(pair.ErrorType))
        {
            tags.Add($"error:{pair.ErrorType}");
        }

        foreach (var field in affectedFields.Take(4))
        {
            tags.Add($"field:{field}");
        }

        if (pair.HttpStatusCodeA != null || pair.HttpStatusCodeB != null)
        {
            tags.Add($"http:{pair.HttpStatusCodeA ?? 0}->{pair.HttpStatusCodeB ?? 0}");
        }

        if (semanticAnalysis != null)
        {
            foreach (var group in semanticAnalysis.SemanticGroups)
            {
                if (!IsSemanticGroupMatch(group, pair))
                {
                    continue;
                }

                var key = $"semantic:{group.GroupName}";
                tags.Add(key);
                if (!tagDescriptions.ContainsKey(key))
                {
                    tagDescriptions[key] = group.SemanticDescription;
                }
            }
        }

        return tags.OrderBy(tag => tag, StringComparer.Ordinal).ToList();
    }

    private static bool IsSemanticGroupMatch(SemanticDifferenceGroup group, FilePairComparisonResult pair)
    {
        if (group.AffectedFiles.Count == 0)
        {
            return false;
        }

        var candidates = new[]
        {
            pair.RequestRelativePath,
            pair.File1Path,
            pair.File2Path,
            pair.File1Name,
            pair.File2Name,
        }.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();

        return candidates.Any(candidate => group.AffectedFiles.Contains(candidate!, StringComparer.OrdinalIgnoreCase));
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

    private static string ResolveChangeType(ChangeType? oldType, ChangeType? newType)
    {
        if (oldType == ChangeType.Inserted || newType == ChangeType.Inserted || oldType == ChangeType.Imaginary)
        {
            return "inserted";
        }

        if (oldType == ChangeType.Deleted || newType == ChangeType.Deleted || newType == ChangeType.Imaginary)
        {
            return "deleted";
        }

        if (oldType == ChangeType.Modified || newType == ChangeType.Modified)
        {
            return "modified";
        }

        return "unchanged";
    }

    private static int? GetLineNumber(DiffPiece? piece)
    {
        return piece == null || piece.Position <= 0 || piece.Type == ChangeType.Imaginary
            ? null
            : piece.Position;
    }

    private static string? GetLineText(DiffPiece? piece)
    {
        return piece == null || piece.Type == ChangeType.Imaginary ? null : piece.Text;
    }

    private static string NormalizeLineEndings(string? value)
    {
        return (value ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
    }

    private static string BuildIndexPath(string dataRootPath)
    {
        return $"{dataRootPath}/index.json";
    }

    private static string BuildChunkPath(string dataRootPath, string chunkId)
    {
        return $"{dataRootPath}/chunks/{chunkId}.json";
    }

    private static async Task WritePayloadAsync(string outputDirectory, string relativePath, object payload)
    {
        var targetPath = Path.Combine(outputDirectory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        var payloadJson = JsonSerializer.Serialize(payload, ComparisonReportJson.CompactOptions);
        await File.WriteAllTextAsync(targetPath, payloadJson);
    }

    private static List<LabelCountDto> BuildStatusCounts(IEnumerable<HtmlReportPairSummaryDto> pairs)
    {
        return pairs
            .GroupBy(
                pair => pair.HasError ? "error" : pair.AreEqual ? "equal" : "different",
                StringComparer.Ordinal)
            .Select(group => new LabelCountDto
            {
                Label = group.Key,
                Count = group.Count(),
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Label, StringComparer.Ordinal)
            .ToList();
    }

    private static string HumanizePatternKey(string key)
    {
        var separatorIndex = key.IndexOf(':');
        if (separatorIndex < 0)
        {
            return key;
        }

        var prefix = key[..separatorIndex];
        var value = key[(separatorIndex + 1)..];
        return prefix switch
        {
            "field" => $"Field: {value}",
            "semantic" => value,
            "outcome" => $"Outcome: {value}",
            "error" => $"Error: {value}",
            "http" => $"HTTP: {value}",
            "kind" => $"Kind: {value}",
            _ => value,
        };
    }

    private static string GetPatternKind(string key)
    {
        var separatorIndex = key.IndexOf(':');
        return separatorIndex < 0 ? "general" : key[..separatorIndex];
    }

    private static T? TryGetMetadata<T>(IReadOnlyDictionary<string, object> metadata, string key)
        where T : class
    {
        if (!metadata.TryGetValue(key, out var value))
        {
            return null;
        }

        return value as T;
    }

    private sealed class WorkingPairDto
    {
        required public HtmlReportPairSummaryDto Summary { get; init; }

        required public HtmlReportPairDetailDto Detail { get; init; }

        required public List<string> PatternKeys { get; init; }
    }

    private sealed class WorkingSummaryDto
    {
        required public HtmlReportPairSummaryDto Summary { get; init; }

        required public List<string> PatternKeys { get; init; }
    }

    private sealed class NormalizedDocumentPairDto
    {
        public string Format { get; init; } = string.Empty;

        public string LeftText { get; init; } = string.Empty;

        public string RightText { get; init; } = string.Empty;
    }
}