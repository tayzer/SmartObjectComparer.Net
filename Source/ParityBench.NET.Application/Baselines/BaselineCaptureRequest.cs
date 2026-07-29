using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// The provenance a capture run supplies up front, before any scenario is recorded.
/// </summary>
public sealed record BaselineCaptureRequest
{
    public BaselineCaptureRequest(
        string name,
        Uri captureEndpoint,
        string pluginId,
        string comparisonId,
        DateTimeOffset capturedAt,
        string capturedFromRunId,
        string? pluginVersion = null,
        string? environmentName = null,
        string? captureEndpointLabel = null,
        string? comparisonRulesSnapshotHash = null,
        ComparisonOptions? comparisonOptions = null)
    {
        ArgumentNullException.ThrowIfNull(captureEndpoint);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline name must not be empty.", nameof(name));
        }

        Name = name.Trim();
        CaptureEndpoint = captureEndpoint;
        PluginId = pluginId;
        ComparisonId = comparisonId;
        CapturedAt = capturedAt;
        CapturedFromRunId = capturedFromRunId;
        PluginVersion = pluginVersion;
        EnvironmentName = environmentName;
        CaptureEndpointLabel = captureEndpointLabel;
        ComparisonRulesSnapshotHash = comparisonRulesSnapshotHash;
        ComparisonOptions = comparisonOptions;
    }

    public string Name { get; }

    public Uri CaptureEndpoint { get; }

    public string PluginId { get; }

    public string ComparisonId { get; }

    public DateTimeOffset CapturedAt { get; }

    public string CapturedFromRunId { get; }

    public string? PluginVersion { get; }

    public string? EnvironmentName { get; }

    public string? CaptureEndpointLabel { get; }

    public string? ComparisonRulesSnapshotHash { get; }

    public ComparisonOptions? ComparisonOptions { get; }
}
