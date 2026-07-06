using System.Text;
using System.Text.Json;

using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.UI.Results;

namespace ParityBench.NET.Report.Results;

public sealed class StaticReportRunResultsViewDataSource : IRunResultsViewDataSource
{
    private const int MaxPreviewBytes = 1024 * 1024;
    private readonly HttpClient httpClient;
    private readonly JsonSerializerOptions jsonOptions;
    private readonly Dictionary<int, StaticReportDetailPage> detailPageCache = new Dictionary<int, StaticReportDetailPage>();
    private StaticReportManifest? manifest;
    private StaticReportDifferenceIndex? differenceIndex;

    public StaticReportRunResultsViewDataSource(HttpClient httpClient)
    {
        this.httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        jsonOptions = StaticReportJsonOptions.Create();
    }

    public async Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        ComparisonRun run = reportManifest.Run.ToRun();
        return new[]
        {
            new RunListItem(
                run.Id,
                run.Status,
                run.CreatedAt,
                run.UpdatedAt,
                run.Progress,
                run.ErrorMessage,
                reportManifest.Summary ?? run.Summary),
        };
    }

    public async Task<ComparisonRun> LoadRunAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        return reportManifest.Run.ToRun();
    }

    public async Task<RunResultSummary?> LoadRunSummaryAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        return reportManifest.Summary ?? reportManifest.Run.Summary;
    }

    public async Task<StaticReportMetadata?> LoadReportMetadataAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        return reportManifest.Metadata ?? StaticReportMetadata.FromRun(reportManifest.Run.ToRun(), reportManifest.GeneratedAt);
    }

    public async Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        return reportManifest.Analysis;
    }


    public async Task<StaticReportDifferenceIndex> LoadDifferenceIndexAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        if (differenceIndex is not null)
        {
            return differenceIndex;
        }

        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);

        string? indexPath = reportManifest.Analysis?.DifferenceIndexPath;
        if (!string.IsNullOrWhiteSpace(indexPath))
        {
            try
            {
                await using Stream stream = await httpClient.GetStreamAsync(indexPath, cancellationToken).ConfigureAwait(false);
                differenceIndex = await JsonSerializer.DeserializeAsync<StaticReportDifferenceIndex>(
                    stream,
                    jsonOptions,
                    cancellationToken).ConfigureAwait(false);
                if (differenceIndex is not null)
                {
                    return differenceIndex;
                }
            }
            catch (HttpRequestException)
            {
                // Older schema-v2 reports may not have the optional sidecar. Fall back to detail pages.
            }
        }

        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(reportManifest, cancellationToken).ConfigureAwait(false);
        differenceIndex = StaticReportDifferenceIndexBuilder.Build(details);
        return differenceIndex;
    }
    public async Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);

        if (query.Outcome is null && query.RelativePathSearch is null)
        {
            return await LoadUnfilteredPageAsync(reportManifest, query, cancellationToken).ConfigureAwait(false);
        }

        return await LoadFilteredPageAsync(reportManifest, query, cancellationToken).ConfigureAwait(false);
    }

    public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default) =>
        ReadArtifactContentAsync(artifact, maxBytes, cancellationToken);

    public async Task<ArtifactContentPreview> ReadArtifactContentAsync(
        ArtifactReference artifact,
        int maxBytes = 512 * 1024,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (maxBytes is < 1 or > MaxPreviewBytes)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), $"Preview size must be between 1 and {MaxPreviewBytes} bytes.");
        }

        await using Stream stream = await httpClient.GetStreamAsync(artifact.ArtifactId, cancellationToken).ConfigureAwait(false);
        byte[] buffer = new byte[maxBytes + 1];
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int bytesRead = await stream
                .ReadAsync(buffer.AsMemory(totalRead, buffer.Length - totalRead), cancellationToken)
                .ConfigureAwait(false);
            if (bytesRead == 0)
            {
                break;
            }

            totalRead += bytesRead;
        }

        bool isTruncated = totalRead > maxBytes;
        int previewBytes = Math.Min(totalRead, maxBytes);
        string content = Encoding.UTF8.GetString(buffer, 0, previewBytes);

        return new ArtifactContentPreview(
            artifact,
            content,
            previewBytes,
            isTruncated,
            artifact.ContentType);
    }

    public async Task<string> ExportRunDetailsJsonAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(reportManifest, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Serialize(details, jsonOptions);
    }

    public async Task<string> ExportRunDetailsCsvAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        StaticReportManifest reportManifest = await LoadManifestAsync(cancellationToken).ConfigureAwait(false);
        EnsureRunMatches(reportManifest, runId);
        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(reportManifest, cancellationToken).ConfigureAwait(false);
        return BuildCsv(details);
    }

    private async Task<RunDetailPage> LoadUnfilteredPageAsync(
        StaticReportManifest reportManifest,
        RunDetailQuery query,
        CancellationToken cancellationToken)
    {
        int totalCount = reportManifest.Summary?.TotalPairs
            ?? reportManifest.Run.Summary?.TotalPairs
            ?? reportManifest.DetailPages.Sum(page => page.ItemCount);
        List<RequestPairResult> items = new List<RequestPairResult>(query.Limit);

        foreach (StaticReportDetailPageInfo pageInfo in reportManifest.DetailPages)
        {
            if (pageInfo.Offset + pageInfo.ItemCount <= query.Offset)
            {
                continue;
            }

            if (items.Count >= query.Limit)
            {
                break;
            }

            StaticReportDetailPage page = await LoadDetailPageAsync(pageInfo, cancellationToken).ConfigureAwait(false);
            int skip = Math.Max(0, query.Offset - pageInfo.Offset);
            items.AddRange(page.Items.Skip(skip).Take(query.Limit - items.Count));
        }

        return new RunDetailPage(items, totalCount, query.Offset, query.Limit);
    }

    private async Task<RunDetailPage> LoadFilteredPageAsync(
        StaticReportManifest reportManifest,
        RunDetailQuery query,
        CancellationToken cancellationToken)
    {
        List<RequestPairResult> items = new List<RequestPairResult>(query.Limit);
        int matchedCount = 0;

        foreach (StaticReportDetailPageInfo pageInfo in reportManifest.DetailPages.OrderBy(page => page.PageIndex))
        {
            StaticReportDetailPage page = await LoadDetailPageAsync(pageInfo, cancellationToken).ConfigureAwait(false);
            foreach (RequestPairResult item in page.Items)
            {
                if (!MatchesQuery(item, query))
                {
                    continue;
                }

                if (matchedCount >= query.Offset && items.Count < query.Limit)
                {
                    items.Add(item);
                }

                matchedCount++;
            }
        }

        return new RunDetailPage(items, matchedCount, query.Offset, query.Limit);
    }

    private async Task<IReadOnlyList<RequestPairResult>> LoadAllDetailsAsync(
        StaticReportManifest reportManifest,
        CancellationToken cancellationToken)
    {
        List<RequestPairResult> details = new List<RequestPairResult>();
        foreach (StaticReportDetailPageInfo pageInfo in reportManifest.DetailPages.OrderBy(page => page.PageIndex))
        {
            StaticReportDetailPage page = await LoadDetailPageAsync(pageInfo, cancellationToken).ConfigureAwait(false);
            details.AddRange(page.Items);
        }

        return details;
    }

    private async Task<StaticReportManifest> LoadManifestAsync(CancellationToken cancellationToken)
    {
        if (manifest is not null)
        {
            return manifest;
        }

        await using Stream stream = await httpClient.GetStreamAsync("report.data.json", cancellationToken).ConfigureAwait(false);
        manifest = await JsonSerializer.DeserializeAsync<StaticReportManifest>(
            stream,
            jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Static report manifest could not be read.");

        return manifest;
    }

    private async Task<StaticReportDetailPage> LoadDetailPageAsync(
        StaticReportDetailPageInfo pageInfo,
        CancellationToken cancellationToken)
    {
        if (detailPageCache.TryGetValue(pageInfo.PageIndex, out StaticReportDetailPage? cachedPage))
        {
            return cachedPage;
        }

        await using Stream stream = await httpClient.GetStreamAsync(pageInfo.Path, cancellationToken).ConfigureAwait(false);
        StaticReportDetailPage page = await JsonSerializer.DeserializeAsync<StaticReportDetailPage>(
            stream,
            jsonOptions,
            cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Static report detail page '{pageInfo.Path}' could not be read.");

        detailPageCache.Add(pageInfo.PageIndex, page);
        return page;
    }

    private static bool MatchesQuery(RequestPairResult item, RunDetailQuery query) =>
        (query.Outcome is null || item.Outcome == query.Outcome.Value)
        && (query.RelativePathSearch is null || item.RelativePath.Contains(query.RelativePathSearch, StringComparison.OrdinalIgnoreCase));

    private static void EnsureRunMatches(
        StaticReportManifest reportManifest,
        RunId runId)
    {
        if (!string.Equals(reportManifest.Run.RunId, runId.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Run '{runId}' was not found in this static report.");
        }
    }

    private static string BuildCsv(IReadOnlyList<RequestPairResult> details)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("Request,Outcome,Differences,StatusA,StatusB,Error");
        foreach (RequestPairResult item in details)
        {
            builder.Append(EscapeCsv(item.RelativePath)).Append(',')
                .Append(EscapeCsv(item.Outcome.ToString())).Append(',')
                .Append(item.DifferenceCount).Append(',')
                .Append(item.ResponseA?.StatusCode.ToString() ?? string.Empty).Append(',')
                .Append(item.ResponseB?.StatusCode.ToString() ?? string.Empty).Append(',')
                .Append(EscapeCsv(item.ErrorMessage ?? item.OutcomeMessage ?? string.Empty))
                .AppendLine();
        }

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (!value.Contains(',') && !value.Contains('"') && !value.Contains('\n') && !value.Contains('\r'))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}
