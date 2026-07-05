using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Reports;

/// <summary>
/// Writes a static bundled report for a completed comparison run.
/// </summary>
public interface IStaticReportBundleWriter
{
    /// <summary>
    /// Writes the report bundle and returns the generated file locations.
    /// </summary>
    Task<StaticReportBundleWriteResult> WriteAsync(
        RunId runId,
        string outputDirectory,
        string reportAssetsDirectory,
        DateTimeOffset? generatedAt = null,
        int detailPageSize = 100,
        CancellationToken cancellationToken = default);
}
