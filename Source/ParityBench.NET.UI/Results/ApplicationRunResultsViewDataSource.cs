using System.Text;
using System.Text.Json;

using ParityBench.NET.Application.Results;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.UI.Results;

public sealed class ApplicationRunResultsViewDataSource : IRunResultsViewDataSource
{
    private readonly IComparisonRunResultUseCases resultUseCases;

    public ApplicationRunResultsViewDataSource(IComparisonRunResultUseCases resultUseCases)
    {
        this.resultUseCases = resultUseCases;
    }

    public Task<IReadOnlyList<RunListItem>> ListRunsAsync(CancellationToken cancellationToken = default) =>
        resultUseCases.ListRunsAsync(cancellationToken);

    public Task<ComparisonRun> LoadRunAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunAsync(runId, cancellationToken);

    public Task<RunResultSummary?> LoadRunSummaryAsync(RunId runId, CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunSummaryAsync(runId, cancellationToken);

    public async Task<StaticReportMetadata?> LoadReportMetadataAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        ComparisonRun run = await resultUseCases.LoadRunAsync(runId, cancellationToken);
        return StaticReportMetadata.FromRun(run, DateTimeOffset.UtcNow);
    }

    public async Task<StaticReportAnalysisSnapshot?> LoadReportAnalysisAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(runId, cancellationToken);
        return BuildAnalysisSnapshot(details);
    }

    public Task<RunDetailPage> LoadRunDetailsAsync(
        RunId runId,
        RunDetailQuery query,
        CancellationToken cancellationToken = default) =>
        resultUseCases.LoadRunDetailsAsync(runId, query, cancellationToken);

    public Task<ArtifactContentPreview> ReadArtifactPreviewAsync(
        ArtifactReference artifact,
        int maxBytes = 64 * 1024,
        CancellationToken cancellationToken = default) =>
        resultUseCases.ReadArtifactPreviewAsync(artifact, maxBytes, cancellationToken);

    public Task<ArtifactContentPreview> ReadArtifactContentAsync(
        ArtifactReference artifact,
        int maxBytes = 512 * 1024,
        CancellationToken cancellationToken = default) =>
        resultUseCases.ReadArtifactPreviewAsync(artifact, maxBytes, cancellationToken);

    public async Task<string> ExportRunDetailsJsonAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(runId, cancellationToken);
        return JsonSerializer.Serialize(details, StaticReportJsonOptions.Create());
    }

    public async Task<string> ExportRunDetailsCsvAsync(
        RunId runId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RequestPairResult> details = await LoadAllDetailsAsync(runId, cancellationToken);
        return BuildCsv(details);
    }

    private async Task<IReadOnlyList<RequestPairResult>> LoadAllDetailsAsync(
        RunId runId,
        CancellationToken cancellationToken)
    {
        List<RequestPairResult> details = new List<RequestPairResult>();
        int offset = 0;
        while (true)
        {
            RunDetailPage page = await resultUseCases.LoadRunDetailsAsync(
                runId,
                new RunDetailQuery(offset, RunDetailQuery.MaxLimit),
                cancellationToken);
            details.AddRange(page.Items);

            if (!page.HasMore)
            {
                break;
            }

            offset += RunDetailQuery.MaxLimit;
        }

        return details;
    }

    private static StaticReportAnalysisSnapshot BuildAnalysisSnapshot(IReadOnlyList<RequestPairResult> items)
    {
        IReadOnlyList<StaticReportDifferenceCategorySummary> categories = items
            .SelectMany(GetPairCategories)
            .GroupBy(category => category, StringComparer.OrdinalIgnoreCase)
            .Select(group => new StaticReportDifferenceCategorySummary(group.Key, group.Key, group.Count(), group.Count()))
            .OrderByDescending(category => category.OccurrenceCount)
            .ToList();

        IReadOnlyList<StaticReportAffectedObjectSummary> affectedObjects = items
            .Where(item => item.Outcome != RequestPairOutcome.Equal)
            .OrderByDescending(item => Math.Max(item.DifferenceCount, item.Differences.Count))
            .Take(25)
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
            categories,
            affectedObjects);
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
            string path = difference.PropertyPath ?? string.Empty;
            if (path.StartsWith("Body.", StringComparison.OrdinalIgnoreCase))
            {
                yield return "Raw Body";
            }
            else if (difference.ValueA is null || difference.ValueB is null)
            {
                yield return "Missing Properties";
            }
            else if (path.Contains('[', StringComparison.Ordinal))
            {
                yield return "Collection / Order";
            }
            else
            {
                yield return "Value Differences";
            }
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
