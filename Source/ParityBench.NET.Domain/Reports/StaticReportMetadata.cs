using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportMetadata
{
    public StaticReportMetadata(
        string reportTitle,
        string runId,
        DateTimeOffset generatedAt,
        string modelName,
        string endpointA,
        string endpointB,
        string? endpointALabel = null,
        string? endpointBLabel = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? startedAt = null,
        DateTimeOffset? completedAt = null,
        DateTimeOffset? updatedAt = null,
        RunExecutionMetrics? executionMetrics = null,
        BaselineReportProvenance? baseline = null)
    {
        if (string.IsNullOrWhiteSpace(runId))
        {
            throw new ArgumentException("Run id must not be empty.", nameof(runId));
        }

        ReportTitle = string.IsNullOrWhiteSpace(reportTitle) ? "Comparison Report" : reportTitle.Trim();
        RunId = runId.Trim();
        GeneratedAt = generatedAt;
        ModelName = string.IsNullOrWhiteSpace(modelName) ? "Auto" : modelName.Trim();
        EndpointA = endpointA ?? string.Empty;
        EndpointB = endpointB ?? string.Empty;
        EndpointALabel = string.IsNullOrWhiteSpace(endpointALabel) ? "Endpoint A" : endpointALabel.Trim();
        EndpointBLabel = string.IsNullOrWhiteSpace(endpointBLabel) ? "Endpoint B" : endpointBLabel.Trim();
        CreatedAt = createdAt;
        StartedAt = startedAt;
        CompletedAt = completedAt;
        UpdatedAt = updatedAt;
        ExecutionMetrics = executionMetrics;
        Baseline = baseline;
    }

    public string ReportTitle { get; }

    public string RunId { get; }

    public DateTimeOffset GeneratedAt { get; }

    public string ModelName { get; }

    public string EndpointA { get; }

    public string EndpointB { get; }

    public string EndpointALabel { get; }

    public string EndpointBLabel { get; }

    public DateTimeOffset? CreatedAt { get; }

    public DateTimeOffset? StartedAt { get; }

    public DateTimeOffset? CompletedAt { get; }

    public DateTimeOffset? UpdatedAt { get; }

    public RunExecutionMetrics? ExecutionMetrics { get; }

    /// <summary>
    /// Gets the baseline provenance for runs that captured or replayed one. Null on a
    /// plain live-vs-live run, and on reports written before baselines existed.
    /// </summary>
    public BaselineReportProvenance? Baseline { get; }

    public TimeSpan? Elapsed =>
        ExecutionMetrics?.TotalDuration
        ?? (StartedAt.HasValue && CompletedAt.HasValue ? CompletedAt.Value - StartedAt.Value : null);

    public static StaticReportMetadata FromRun(
        ComparisonRun run,
        DateTimeOffset generatedAt,
        BaselineReportProvenance? baseline = null)
    {
        ArgumentNullException.ThrowIfNull(run);

        return new StaticReportMetadata(
            baseline?.ModeLabel is { Length: > 0 } modeLabel && baseline.Mode != BaselineRunMode.LiveVsLive
                ? $"{modeLabel} Report"
                : "Comparison Report",
            run.Id.Value,
            generatedAt,
            run.Options.ModelName,
            run.Options.EndpointA.Uri.ToString(),
            run.Options.EndpointB.Uri.ToString(),
            run.Options.EndpointA.Label,
            run.Options.EndpointB.Label,
            run.CreatedAt,
            run.StartedAt,
            run.CompletedAt,
            run.UpdatedAt,
            run.Summary?.ExecutionMetrics,
            baseline);
    }
}
