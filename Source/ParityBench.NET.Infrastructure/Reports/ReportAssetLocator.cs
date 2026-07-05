using ParityBench.NET.Application.Reports;

namespace ParityBench.NET.Infrastructure.Reports;

public sealed class ReportAssetLocator : IReportAssetLocator
{
    private const string ReportAssetsDirectoryName = "BlazorReportAssets";

    public string Resolve(string? configuredReportAssetsDirectory = null)
    {
        foreach (string candidate in GetCandidateDirectories(configuredReportAssetsDirectory))
        {
            if (IsUsableAssetDirectory(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(
            $"Static report assets were not found. Searched: {string.Join(", ", GetCandidateDirectories(configuredReportAssetsDirectory))}");
    }

    private static IEnumerable<string> GetCandidateDirectories(string? configuredReportAssetsDirectory)
    {
        if (!string.IsNullOrWhiteSpace(configuredReportAssetsDirectory))
        {
            yield return configuredReportAssetsDirectory;
        }

        yield return Path.Combine(AppContext.BaseDirectory, ReportAssetsDirectoryName);
        yield return Path.Combine(Directory.GetCurrentDirectory(), ReportAssetsDirectoryName);
    }

    private static bool IsUsableAssetDirectory(string directory) =>
        Directory.Exists(directory)
        && File.Exists(Path.Combine(directory, "index.html"))
        && Directory.Exists(Path.Combine(directory, "_framework"));
}
