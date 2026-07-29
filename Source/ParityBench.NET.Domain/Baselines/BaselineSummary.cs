namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// A listing entry for the baseline library: enough to choose a package without
/// reading every scenario in it.
/// </summary>
public sealed record BaselineSummary(
    BaselineId Id,
    string Name,
    int Version,
    DateTimeOffset CapturedAt,
    Uri CaptureEndpoint,
    string PluginId,
    string ComparisonId,
    string? PluginVersion,
    string? EnvironmentName,
    int ScenarioCount,
    long TotalBytes)
{
    public string DisplayVersion => $"v{Version}";

    public static BaselineSummary FromManifest(BaselinePackageManifest manifest, long totalBytes)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        return new BaselineSummary(
            manifest.Id,
            manifest.Name,
            manifest.Version,
            manifest.CapturedAt,
            manifest.CaptureEndpoint,
            manifest.PluginId,
            manifest.ComparisonId,
            manifest.PluginVersion,
            manifest.EnvironmentName,
            manifest.Scenarios.Count,
            totalBytes);
    }
}
