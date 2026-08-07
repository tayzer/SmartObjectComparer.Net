using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// The provenance record of a captured baseline. A replay may happen months later in
/// a different environment, so everything needed to judge whether a difference is a
/// software regression or an environmental change is recorded here.
/// </summary>
public sealed record BaselinePackageManifest
{
    public const int CurrentSchemaVersion = 1;

    public BaselinePackageManifest(
        BaselineId id,
        string name,
        int version,
        DateTimeOffset capturedAt,
        string capturedFromRunId,
        Uri captureEndpoint,
        string pluginId,
        string comparisonId,
        string? pluginVersion = null,
        string? environmentName = null,
        string? captureEndpointLabel = null,
        string? comparisonRulesSnapshotHash = null,
        ComparisonOptions? comparisonOptions = null,
        string? toolVersion = null,
        string? capturedBy = null,
        string? capturedOnMachine = null,
        IEnumerable<BaselineScenarioEntry>? scenarios = null,
        int schemaVersion = CurrentSchemaVersion)
    {
        ArgumentNullException.ThrowIfNull(captureEndpoint);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline name must not be empty.", nameof(name));
        }

        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Baseline version must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("Plugin id must not be empty.", nameof(pluginId));
        }

        if (string.IsNullOrWhiteSpace(comparisonId))
        {
            throw new ArgumentException("Comparison id must not be empty.", nameof(comparisonId));
        }

        Id = id;
        Name = name.Trim();
        Version = version;
        CapturedAt = capturedAt;
        CapturedFromRunId = capturedFromRunId ?? string.Empty;
        CaptureEndpoint = captureEndpoint;
        CaptureEndpointLabel = string.IsNullOrWhiteSpace(captureEndpointLabel) ? null : captureEndpointLabel.Trim();
        PluginId = pluginId.Trim();
        ComparisonId = comparisonId.Trim();
        PluginVersion = string.IsNullOrWhiteSpace(pluginVersion) ? null : pluginVersion.Trim();
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName.Trim();
        ComparisonRulesSnapshotHash = string.IsNullOrWhiteSpace(comparisonRulesSnapshotHash)
            ? null
            : comparisonRulesSnapshotHash.Trim();
        ComparisonOptions = comparisonOptions ?? new ComparisonOptions();
        ToolVersion = string.IsNullOrWhiteSpace(toolVersion) ? null : toolVersion.Trim();
        CapturedBy = string.IsNullOrWhiteSpace(capturedBy) ? null : capturedBy.Trim();
        CapturedOnMachine = string.IsNullOrWhiteSpace(capturedOnMachine) ? null : capturedOnMachine.Trim();
        Scenarios = (scenarios ?? Array.Empty<BaselineScenarioEntry>()).ToArray();
        SchemaVersion = schemaVersion;
    }

    public int SchemaVersion { get; }

    public BaselineId Id { get; }

    public string Name { get; }

    public int Version { get; }

    public DateTimeOffset CapturedAt { get; }

    public string CapturedFromRunId { get; }

    public Uri CaptureEndpoint { get; }

    public string? CaptureEndpointLabel { get; }

    public string PluginId { get; }

    public string ComparisonId { get; }

    public string? PluginVersion { get; }

    public string? EnvironmentName { get; }

    public string? ComparisonRulesSnapshotHash { get; }

    /// <summary>Gets the comparison settings in force when the baseline was captured.</summary>
    public ComparisonOptions ComparisonOptions { get; }

    public string? ToolVersion { get; }

    public string? CapturedBy { get; }

    public string? CapturedOnMachine { get; }

    public IReadOnlyList<BaselineScenarioEntry> Scenarios { get; }

    public string DisplayVersion => $"v{Version}";

    public BaselinePackageManifest WithScenarios(IEnumerable<BaselineScenarioEntry> scenarios) =>
        new BaselinePackageManifest(
            Id,
            Name,
            Version,
            CapturedAt,
            CapturedFromRunId,
            CaptureEndpoint,
            PluginId,
            ComparisonId,
            PluginVersion,
            EnvironmentName,
            CaptureEndpointLabel,
            ComparisonRulesSnapshotHash,
            ComparisonOptions,
            ToolVersion,
            CapturedBy,
            CapturedOnMachine,
            scenarios,
            SchemaVersion);
}
