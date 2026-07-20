namespace ParityBench.NET.Application.Reports;

/// <summary>
/// Resolves the published static report assets used when generating bundled reports.
/// </summary>
public interface IReportAssetLocator
{
    /// <summary>
    /// Resolves a usable report asset directory.
    /// </summary>
    string Resolve(string? configuredReportAssetsDirectory = null);
}
