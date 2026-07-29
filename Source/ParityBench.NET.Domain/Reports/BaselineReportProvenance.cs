using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Baselines;

namespace ParityBench.NET.Domain.Reports;

/// <summary>
/// What a report needs to say about a run that involved a baseline.
/// </summary>
/// <remarks>
/// A baseline is replayed at a different time, often in a different environment, from
/// the capture it is compared against. The report has to make that visible, or a
/// difference caused by changed data or configuration reads as a software regression.
/// </remarks>
public sealed record BaselineReportProvenance
{
    public BaselineReportProvenance(
        BaselineRunMode mode,
        string? baselineId = null,
        string? baselineName = null,
        int? baselineVersion = null,
        DateTimeOffset? capturedAt = null,
        string? captureEndpoint = null,
        string? captureEndpointLabel = null,
        string? capturePluginId = null,
        string? capturePluginVersion = null,
        string? captureComparisonId = null,
        string? captureEnvironment = null,
        string? capturedFromRunId = null,
        string? captureToolVersion = null,
        string? livePluginVersion = null,
        string? liveEnvironment = null,
        string? liveToolVersion = null,
        int scenarioCount = 0)
    {
        Mode = mode;
        BaselineId = baselineId;
        BaselineName = baselineName;
        BaselineVersion = baselineVersion;
        CapturedAt = capturedAt;
        CaptureEndpoint = captureEndpoint;
        CaptureEndpointLabel = captureEndpointLabel;
        CapturePluginId = capturePluginId;
        CapturePluginVersion = capturePluginVersion;
        CaptureComparisonId = captureComparisonId;
        CaptureEnvironment = captureEnvironment;
        CapturedFromRunId = capturedFromRunId;
        CaptureToolVersion = captureToolVersion;
        LivePluginVersion = livePluginVersion;
        LiveEnvironment = liveEnvironment;
        LiveToolVersion = liveToolVersion;
        ScenarioCount = scenarioCount;
    }

    public BaselineRunMode Mode { get; }

    public string? BaselineId { get; }

    public string? BaselineName { get; }

    public int? BaselineVersion { get; }

    public DateTimeOffset? CapturedAt { get; }

    public string? CaptureEndpoint { get; }

    public string? CaptureEndpointLabel { get; }

    public string? CapturePluginId { get; }

    public string? CapturePluginVersion { get; }

    public string? CaptureComparisonId { get; }

    public string? CaptureEnvironment { get; }

    public string? CapturedFromRunId { get; }

    public string? CaptureToolVersion { get; }

    public string? LivePluginVersion { get; }

    public string? LiveEnvironment { get; }

    public string? LiveToolVersion { get; }

    public int ScenarioCount { get; }

    [JsonIgnore]
    public string DisplayVersion => BaselineVersion is null ? string.Empty : $"v{BaselineVersion}";

    [JsonIgnore]
    public string DisplayName => string.IsNullOrWhiteSpace(BaselineName)
        ? BaselineId ?? string.Empty
        : BaselineName;

    [JsonIgnore]
    public string ModeLabel => Mode switch
    {
        BaselineRunMode.CaptureBaseline => "Baseline Capture",
        BaselineRunMode.BaselineVsLive => "Baseline vs Live",
        _ => "Live vs Live",
    };

    /// <summary>
    /// Gets a value indicating whether the plugin that mapped the baseline differs
    /// from the one that mapped the live side — the mapping itself may have changed,
    /// which is not the same thing as the endpoint's behaviour changing.
    /// </summary>
    [JsonIgnore]
    public bool PluginVersionChanged =>
        Mode == BaselineRunMode.BaselineVsLive
        && !string.IsNullOrWhiteSpace(CapturePluginVersion)
        && !string.IsNullOrWhiteSpace(LivePluginVersion)
        && !string.Equals(CapturePluginVersion, LivePluginVersion, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool EnvironmentChanged =>
        Mode == BaselineRunMode.BaselineVsLive
        && !string.IsNullOrWhiteSpace(CaptureEnvironment)
        && !string.IsNullOrWhiteSpace(LiveEnvironment)
        && !string.Equals(CaptureEnvironment, LiveEnvironment, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool HasProvenanceWarning => PluginVersionChanged || EnvironmentChanged;
}
