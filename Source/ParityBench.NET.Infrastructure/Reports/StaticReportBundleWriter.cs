using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using ParityBench.NET.Application.AcceptedDifferences;
using ParityBench.NET.Application.Reports;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Infrastructure.Reports;

public sealed class StaticReportBundleWriter : IStaticReportBundleWriter
{
    private const string ManifestFileName = "report.data.json";
    private const string DetailsDirectoryName = "details";
    private const string RawDirectoryName = "raw";
    private const string AnalysisDirectoryName = "analysis";
    private const string DifferenceIndexFileName = "difference-index.json";
    private const int TopAffectedObjectLimit = 25;
    private readonly IComparisonRunResultUseCases resultUseCases;
    private readonly IRunArtifactStore artifactStore;
    private readonly IAcceptedDifferenceUseCases? acceptedDifferenceUseCases;
    private readonly JsonSerializerOptions jsonOptions;

    public StaticReportBundleWriter(
        IComparisonRunResultUseCases resultUseCases,
        IRunArtifactStore artifactStore,
        IAcceptedDifferenceUseCases? acceptedDifferenceUseCases = null)
    {
        this.resultUseCases = resultUseCases ?? throw new ArgumentNullException(nameof(resultUseCases));
        this.artifactStore = artifactStore ?? throw new ArgumentNullException(nameof(artifactStore));
        this.acceptedDifferenceUseCases = acceptedDifferenceUseCases;
        jsonOptions = StaticReportJsonOptions.Create();
    }

    public async Task<StaticReportBundleResult> WriteAsync(
        RunId runId,
        string outputDirectory,
        string reportAssetsDirectory,
        DateTimeOffset? generatedAt = null,
        int detailPageSize = StaticReportManifest.DefaultDetailPageSize,
        CancellationToken cancellationToken = default)
    {
        string normalizedOutputDirectory = NormalizeDirectory(outputDirectory, nameof(outputDirectory));
        string normalizedAssetsDirectory = NormalizeDirectory(reportAssetsDirectory, nameof(reportAssetsDirectory));
        ValidateDetailPageSize(detailPageSize);
        ValidateReportAssets(normalizedAssetsDirectory);

        Directory.CreateDirectory(normalizedOutputDirectory);
        CopyDirectory(normalizedAssetsDirectory, normalizedOutputDirectory);

        string detailsDirectory = Path.Combine(normalizedOutputDirectory, DetailsDirectoryName);
        string rawDirectory = Path.Combine(normalizedOutputDirectory, RawDirectoryName);
        string analysisDirectory = Path.Combine(normalizedOutputDirectory, AnalysisDirectoryName);
        Directory.CreateDirectory(detailsDirectory);
        Directory.CreateDirectory(rawDirectory);
        Directory.CreateDirectory(analysisDirectory);

        ComparisonRun run = await resultUseCases.LoadRunAsync(runId, cancellationToken).ConfigureAwait(false);
        RunResultSummary? summary = await resultUseCases.LoadRunSummaryAsync(runId, cancellationToken).ConfigureAwait(false);
        List<StaticReportDetailPageInfo> pageInfos = new List<StaticReportDetailPageInfo>();
        Dictionary<string, ArtifactReference> copiedArtifacts = new Dictionary<string, ArtifactReference>(StringComparer.Ordinal);
        List<RequestPairResult> analysisItems = new List<RequestPairResult>();

        int offset = 0;
        int pageIndex = 0;
        while (true)
        {
            RunDetailPage detailPage = await resultUseCases
                .LoadRunDetailsAsync(runId, new RunDetailQuery(offset, detailPageSize), cancellationToken)
                .ConfigureAwait(false);

            if (detailPage.Items.Count == 0)
            {
                break;
            }

            List<RequestPairResult> rewrittenItems = new List<RequestPairResult>(detailPage.Items.Count);
            foreach (RequestPairResult item in detailPage.Items)
            {
                RequestPairResult rewrittenItem = await RewriteArtifactsAsync(
                    item,
                    normalizedOutputDirectory,
                    copiedArtifacts,
                    cancellationToken).ConfigureAwait(false);
                rewrittenItems.Add(rewrittenItem);
                analysisItems.Add(rewrittenItem);
            }

            string pageFileName = $"page-{pageIndex.ToString("D6", CultureInfo.InvariantCulture)}.json";
            string pagePath = Path.Combine(detailsDirectory, pageFileName);
            StaticReportDetailPage staticPage = new StaticReportDetailPage(
                pageIndex,
                detailPage.Offset,
                detailPage.TotalCount,
                rewrittenItems);
            await WriteJsonAsync(pagePath, staticPage, cancellationToken).ConfigureAwait(false);
            pageInfos.Add(new StaticReportDetailPageInfo(
                pageIndex,
                detailPage.Offset,
                rewrittenItems.Count,
                $"{DetailsDirectoryName}/{pageFileName}"));

            if (!detailPage.HasMore)
            {
                break;
            }

            offset += detailPageSize;
            pageIndex++;
        }

        string differenceIndexPath = $"{AnalysisDirectoryName}/{DifferenceIndexFileName}";
        StaticReportDifferenceIndex differenceIndex = StaticReportDifferenceIndexBuilder.Build(analysisItems);
        await WriteJsonAsync(Path.Combine(analysisDirectory, DifferenceIndexFileName), differenceIndex, cancellationToken).ConfigureAwait(false);

        AcceptedDifferenceProfileStore? acceptedDifferenceSnapshot = acceptedDifferenceUseCases is null
            ? null
            : new AcceptedDifferenceProfileStore
            {
                Profiles = (await acceptedDifferenceUseCases.ListAsync(cancellationToken).ConfigureAwait(false)).ToList(),
            };

        DateTimeOffset generatedAtValue = generatedAt ?? DateTimeOffset.UtcNow;
        StaticReportManifest manifest = new StaticReportManifest(
            StaticReportManifest.CurrentSchemaVersion,
            generatedAtValue,
            StaticReportRunSnapshot.FromRun(run),
            summary,
            detailPageSize,
            pageInfos,
            StaticReportMetadata.FromRun(run, generatedAtValue),
            BuildAnalysisSnapshot(analysisItems, differenceIndexPath),
            acceptedDifferenceSnapshot);

        string manifestPath = Path.Combine(normalizedOutputDirectory, ManifestFileName);
        await WriteJsonAsync(manifestPath, manifest, cancellationToken).ConfigureAwait(false);
        await WriteLaunchersAsync(normalizedOutputDirectory, cancellationToken).ConfigureAwait(false);
        string redirectorPath = await WriteRedirectorAsync(normalizedOutputDirectory, cancellationToken).ConfigureAwait(false);

        return new StaticReportBundleResult(
            normalizedOutputDirectory,
            manifestPath,
            redirectorPath,
            pageInfos.Count,
            copiedArtifacts.Count);
    }

    async Task<StaticReportBundleWriteResult> IStaticReportBundleWriter.WriteAsync(
        RunId runId,
        string outputDirectory,
        string reportAssetsDirectory,
        DateTimeOffset? generatedAt,
        int detailPageSize,
        CancellationToken cancellationToken)
    {
        StaticReportBundleResult result = await WriteAsync(
            runId,
            outputDirectory,
            reportAssetsDirectory,
            generatedAt,
            detailPageSize,
            cancellationToken).ConfigureAwait(false);

        return new StaticReportBundleWriteResult(
            result.OutputDirectory,
            result.ManifestPath,
            result.RedirectorPath,
            result.DetailPageCount,
            result.RawArtifactCount);
    }

    private async Task<RequestPairResult> RewriteArtifactsAsync(
        RequestPairResult item,
        string outputDirectory,
        Dictionary<string, ArtifactReference> copiedArtifacts,
        CancellationToken cancellationToken)
    {
        ResponseArtifactMetadata? responseA = await RewriteArtifactAsync(
            item.ResponseA,
            outputDirectory,
            copiedArtifacts,
            cancellationToken).ConfigureAwait(false);
        ResponseArtifactMetadata? responseB = await RewriteArtifactAsync(
            item.ResponseB,
            outputDirectory,
            copiedArtifacts,
            cancellationToken).ConfigureAwait(false);
        ResponseArtifactMetadata? focusedResponseA = await RewriteArtifactAsync(
            item.FocusedResponseA,
            outputDirectory,
            copiedArtifacts,
            cancellationToken).ConfigureAwait(false);
        ResponseArtifactMetadata? focusedResponseB = await RewriteArtifactAsync(
            item.FocusedResponseB,
            outputDirectory,
            copiedArtifacts,
            cancellationToken).ConfigureAwait(false);

        return new RequestPairResult(
            item.RelativePath,
            item.Outcome,
            responseA,
            responseB,
            item.ErrorMessage,
            item.AreEqual,
            item.DifferenceCount,
            item.Differences,
            item.OutcomeMessage,
            BuildRawTextDifferences(item, responseA, responseB),
            focusedResponseA,
            focusedResponseB,
            item.FocusedRawContentIgnorePaths);
    }

    private async Task<ResponseArtifactMetadata?> RewriteArtifactAsync(
        ResponseArtifactMetadata? response,
        string outputDirectory,
        Dictionary<string, ArtifactReference> copiedArtifacts,
        CancellationToken cancellationToken)
    {
        if (response is null)
        {
            return null;
        }

        if (!copiedArtifacts.TryGetValue(response.Artifact.ArtifactId, out ArtifactReference? rewrittenArtifact))
        {
            string sidecarPath = $"{RawDirectoryName}/{CreateSafeArtifactFileName(response.Artifact.ArtifactId)}";
            rewrittenArtifact = new ArtifactReference(sidecarPath, response.Artifact.ContentType);
            copiedArtifacts.Add(response.Artifact.ArtifactId, rewrittenArtifact);

            string destinationPath = Path.Combine(outputDirectory, sidecarPath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? outputDirectory);
            await using Stream source = await artifactStore.OpenReadAsync(response.Artifact, cancellationToken).ConfigureAwait(false);
            await using FileStream destination = new FileStream(
                destinationPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                81920,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            await source.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
        }

        return new ResponseArtifactMetadata(
            response.Endpoint,
            rewrittenArtifact,
            response.StatusCode,
            response.ContentType,
            response.ContentLength,
            response.Sha256);
    }

    private static IReadOnlyList<StaticReportRawTextDifference> BuildRawTextDifferences(
        RequestPairResult item,
        ResponseArtifactMetadata? responseA,
        ResponseArtifactMetadata? responseB)
    {
        if (item.Outcome is not (RequestPairOutcome.StatusCodeMismatch or RequestPairOutcome.BothNonSuccess))
        {
            return item.RawTextDifferences;
        }

        List<StaticReportRawTextDifference> rows = new List<StaticReportRawTextDifference>();
        if (responseA is not null && responseB is not null && responseA.StatusCode != responseB.StatusCode)
        {
            rows.Add(new StaticReportRawTextDifference(
                StaticReportRawTextDifferenceType.StatusCodeDifference,
                textA: responseA.StatusCode.ToString(CultureInfo.InvariantCulture),
                textB: responseB.StatusCode.ToString(CultureInfo.InvariantCulture),
                message: item.OutcomeMessage));
        }

        foreach (ComparisonDifference difference in item.Differences)
        {
            if (string.Equals(difference.PropertyPath, "HttpStatus", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(difference.PropertyPath, "BodyPreview", StringComparison.OrdinalIgnoreCase))
            {
                rows.Add(new StaticReportRawTextDifference(
                    StaticReportRawTextDifferenceType.PreviewTruncated,
                    textA: difference.ValueA,
                    textB: difference.ValueB,
                    message: difference.Message));
                continue;
            }

            if (TryParseBodyLineNumber(difference.PropertyPath, out int lineNumber))
            {
                StaticReportRawTextDifferenceType type = difference.ValueA is null
                    ? StaticReportRawTextDifferenceType.OnlyInB
                    : difference.ValueB is null
                        ? StaticReportRawTextDifferenceType.OnlyInA
                        : StaticReportRawTextDifferenceType.Modified;
                rows.Add(new StaticReportRawTextDifference(
                    type,
                    difference.ValueA is null ? null : lineNumber,
                    difference.ValueB is null ? null : lineNumber,
                    difference.ValueA,
                    difference.ValueB,
                    difference.Message));
            }
        }

        return rows;
    }

    private static bool TryParseBodyLineNumber(
        string propertyPath,
        out int lineNumber)
    {
        const string prefix = "Body.Line[";
        lineNumber = 0;
        if (!propertyPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) || !propertyPath.EndsWith("]", StringComparison.Ordinal))
        {
            return false;
        }

        string numberText = propertyPath.Substring(prefix.Length, propertyPath.Length - prefix.Length - 1);
        return int.TryParse(numberText, NumberStyles.None, CultureInfo.InvariantCulture, out lineNumber) && lineNumber > 0;
    }

    private static StaticReportAnalysisSnapshot BuildAnalysisSnapshot(IReadOnlyList<RequestPairResult> items, string? differenceIndexPath = null)
    {
        Dictionary<string, CategoryAccumulator> categories = new Dictionary<string, CategoryAccumulator>(StringComparer.OrdinalIgnoreCase);
        foreach (RequestPairResult item in items)
        {
            List<string> pairCategories = GetPairCategories(item).ToList();
            if (pairCategories.Count == 0)
            {
                pairCategories.Add(item.Outcome == RequestPairOutcome.Equal ? "Equal" : "Other");
            }

            foreach (string category in pairCategories.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (!categories.TryGetValue(category, out CategoryAccumulator? accumulator))
                {
                    accumulator = new CategoryAccumulator(category);
                    categories.Add(category, accumulator);
                }

                accumulator.AffectedPairCount++;
                accumulator.OccurrenceCount += Math.Max(1, item.Differences.Count(difference => string.Equals(StaticReportDifferenceIndexBuilder.CategorizeDifference(difference), category, StringComparison.OrdinalIgnoreCase)));
            }
        }

        IReadOnlyList<StaticReportDifferenceCategorySummary> categorySummaries = categories.Values
            .OrderByDescending(category => category.OccurrenceCount)
            .ThenBy(category => category.Category, StringComparer.OrdinalIgnoreCase)
            .Select(category => new StaticReportDifferenceCategorySummary(
                category.Category,
                category.Category,
                category.OccurrenceCount,
                category.AffectedPairCount))
            .ToList();

        IReadOnlyList<StaticReportAffectedObjectSummary> affectedObjects = items
            .Where(item => item.Outcome != RequestPairOutcome.Equal)
            .OrderByDescending(item => Math.Max(item.DifferenceCount, item.Differences.Count))
            .ThenBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase)
            .Take(TopAffectedObjectLimit)
            .Select(item => new StaticReportAffectedObjectSummary(
                item.RelativePath,
                Math.Max(item.DifferenceCount, item.Differences.Count),
                GetPairCategories(item).FirstOrDefault() ?? "Other",
                item.Outcome.ToString()))
            .ToList();

        return new StaticReportAnalysisSnapshot(
            items.Count,
            items.Count(item => item.Outcome != RequestPairOutcome.ExecutionFailed),
            items.Count(item => item.Outcome is RequestPairOutcome.Different or RequestPairOutcome.StatusCodeMismatch or RequestPairOutcome.BothNonSuccess),
            items.Count(item => item.Outcome == RequestPairOutcome.ExecutionFailed),
            items.Sum(item => item.DifferenceCount),
            categorySummaries,
            affectedObjects,
            differenceIndexPath);
    }

    private static IEnumerable<string> GetPairCategories(RequestPairResult item)
    {
        if (item.Outcome == RequestPairOutcome.Equal)
        {
            yield return "Equal";
            yield break;
        }

        if (item.Outcome == RequestPairOutcome.ExecutionFailed)
        {
            yield return "Errors";
            yield break;
        }

        if (item.Outcome == RequestPairOutcome.StatusCodeMismatch)
        {
            yield return "HTTP Status";
        }

        if (item.Outcome == RequestPairOutcome.BothNonSuccess)
        {
            yield return "Non-Success";
        }

        foreach (ComparisonDifference difference in item.Differences)
        {
            yield return StaticReportDifferenceIndexBuilder.CategorizeDifference(difference);
        }
    }

    private async Task WriteJsonAsync<T>(
        string path,
        T value,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new FileStream(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        await JsonSerializer.SerializeAsync(stream, value, jsonOptions, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteLaunchersAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        const string windowsLauncher = """
            @echo off
            set PORT=%1
            if "%PORT%"=="" set PORT=5088
            python -m http.server %PORT%
            """;
        const string unixLauncher = """
            #!/usr/bin/env sh
            PORT="${1:-5088}"
            python3 -m http.server "$PORT"
            """;

        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "serve.cmd"), windowsLauncher, cancellationToken).ConfigureAwait(false);
        await File.WriteAllTextAsync(Path.Combine(outputDirectory, "serve.sh"), unixLauncher, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> WriteRedirectorAsync(
        string outputDirectory,
        CancellationToken cancellationToken)
    {
        DirectoryInfo outputInfo = new DirectoryInfo(outputDirectory);
        string parentDirectory = outputInfo.Parent?.FullName ?? outputDirectory;
        string redirectorPath = Path.Combine(parentDirectory, $"{outputInfo.Name}.html");
        string target = $"{outputInfo.Name}/index.html";
        string html = $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <meta http-equiv="refresh" content="0; url={{target}}">
              <title>ParityBench.NET Report</title>
            </head>
            <body>
              <a href="{{target}}">Open ParityBench.NET report</a>
            </body>
            </html>
            """;

        await File.WriteAllTextAsync(redirectorPath, html, cancellationToken).ConfigureAwait(false);
        return redirectorPath;
    }

    private static string CreateSafeArtifactFileName(string artifactId)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(artifactId));
        return $"{Convert.ToHexString(bytes).ToLowerInvariant()}.body";
    }

    private static string NormalizeDirectory(string directory, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ArgumentException("Directory path must not be empty.", parameterName);
        }

        return Path.GetFullPath(directory);
    }

    private static void ValidateDetailPageSize(int detailPageSize)
    {
        if (detailPageSize is < 1 or > RunDetailQuery.MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(detailPageSize), $"Detail page size must be between 1 and {RunDetailQuery.MaxLimit}.");
        }
    }

    private static void ValidateReportAssets(string reportAssetsDirectory)
    {
        if (!Directory.Exists(reportAssetsDirectory))
        {
            throw new InvalidOperationException($"Report assets directory '{reportAssetsDirectory}' does not exist.");
        }

        if (!File.Exists(Path.Combine(reportAssetsDirectory, "index.html")))
        {
            throw new InvalidOperationException($"Report assets directory '{reportAssetsDirectory}' is missing index.html.");
        }

        if (!Directory.Exists(Path.Combine(reportAssetsDirectory, "_framework")))
        {
            throw new InvalidOperationException($"Report assets directory '{reportAssetsDirectory}' is missing _framework assets.");
        }
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (string sourceFile in Directory.EnumerateFiles(sourceDirectory))
        {
            string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
            File.Copy(sourceFile, destinationFile, overwrite: true);
        }

        foreach (string sourceSubDirectory in Directory.EnumerateDirectories(sourceDirectory))
        {
            string destinationSubDirectory = Path.Combine(destinationDirectory, Path.GetFileName(sourceSubDirectory));
            CopyDirectory(sourceSubDirectory, destinationSubDirectory);
        }
    }

    private sealed class CategoryAccumulator
    {
        public CategoryAccumulator(string category)
        {
            Category = category;
        }

        public string Category { get; }

        public int OccurrenceCount { get; set; }

        public int AffectedPairCount { get; set; }
    }
}

