using ParityBench.NET.Domain.AcceptedDifferences;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportManifest
{
    // 3 added baseline provenance to the metadata. Older bundles still load: the
    // provenance is simply absent, which is exactly what a live-vs-live run means.
    public const int CurrentSchemaVersion = 3;

    public const int MinimumSupportedSchemaVersion = 1;

    public const int DefaultDetailPageSize = 100;

    public StaticReportManifest(
        int schemaVersion,
        DateTimeOffset generatedAt,
        StaticReportRunSnapshot run,
        RunResultSummary? summary,
        int detailPageSize,
        IReadOnlyList<StaticReportDetailPageInfo>? detailPages = null,
        StaticReportMetadata? metadata = null,
        StaticReportAnalysisSnapshot? analysis = null,
        AcceptedDifferenceProfileStore? acceptedDifferences = null)
    {
        if (schemaVersion is < MinimumSupportedSchemaVersion or > CurrentSchemaVersion)
        {
            throw new ArgumentOutOfRangeException(
                nameof(schemaVersion),
                $"Static report schema version must be between {MinimumSupportedSchemaVersion} and {CurrentSchemaVersion}.");
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
        Metadata = metadata;
        Analysis = analysis;
        AcceptedDifferences = acceptedDifferences;
    }

    public int SchemaVersion { get; }

    public DateTimeOffset GeneratedAt { get; }

    public StaticReportRunSnapshot Run { get; }

    public RunResultSummary? Summary { get; }

    public int DetailPageSize { get; }

    public IReadOnlyList<StaticReportDetailPageInfo> DetailPages { get; }

    public StaticReportMetadata? Metadata { get; }

    public StaticReportAnalysisSnapshot? Analysis { get; }

    public AcceptedDifferenceProfileStore? AcceptedDifferences { get; }
}