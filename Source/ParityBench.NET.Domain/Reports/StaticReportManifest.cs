using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportManifest
{
    public const int CurrentSchemaVersion = 1;

    public const int DefaultDetailPageSize = 100;

    public StaticReportManifest(
        int schemaVersion,
        DateTimeOffset generatedAt,
        StaticReportRunSnapshot run,
        RunResultSummary? summary,
        int detailPageSize,
        IReadOnlyList<StaticReportDetailPageInfo>? detailPages = null)
    {
        if (schemaVersion != CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), $"Static report schema version must be {CurrentSchemaVersion}.");
        }

        ArgumentNullException.ThrowIfNull(run);

        if (detailPageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detailPageSize), "Detail page size must be positive.");
        }

        SchemaVersion = schemaVersion;
        GeneratedAt = generatedAt;
        Run = run;
        Summary = summary;
        DetailPageSize = detailPageSize;
        DetailPages = (detailPages ?? Array.Empty<StaticReportDetailPageInfo>()).ToList();
    }

    public int SchemaVersion { get; }

    public DateTimeOffset GeneratedAt { get; }

    public StaticReportRunSnapshot Run { get; }

    public RunResultSummary? Summary { get; }

    public int DetailPageSize { get; }

    public IReadOnlyList<StaticReportDetailPageInfo> DetailPages { get; }
}
