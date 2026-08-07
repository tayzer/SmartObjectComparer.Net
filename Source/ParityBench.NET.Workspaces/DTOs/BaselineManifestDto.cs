using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Comparison;

namespace ParityBench.NET.Workspaces;

/// <summary>
/// On-disk form of a baseline package manifest. Kept hand-readable: a client may
/// check a package into their own repository next to the code it verifies.
/// </summary>
internal sealed class BaselineManifestDto
{
    public int SchemaVersion { get; set; } = BaselinePackageManifest.CurrentSchemaVersion;

    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public int Version { get; set; }

    public DateTimeOffset CapturedAt { get; set; }

    public string CapturedFromRunId { get; set; } = string.Empty;

    public string CaptureEndpoint { get; set; } = string.Empty;

    public string? CaptureEndpointLabel { get; set; }

    public string PluginId { get; set; } = string.Empty;

    public string ComparisonId { get; set; } = string.Empty;

    public string? PluginVersion { get; set; }

    public string? Environment { get; set; }

    public string? ComparisonRulesSnapshotHash { get; set; }

    public BaselineComparisonOptionsDto? Comparison { get; set; }

    public string? ToolVersion { get; set; }

    public string? CapturedBy { get; set; }

    public string? CapturedOnMachine { get; set; }

    public List<BaselineScenarioEntryDto> Scenarios { get; set; } = new List<BaselineScenarioEntryDto>();

    public static BaselineManifestDto FromManifest(BaselinePackageManifest manifest) => new BaselineManifestDto
    {
        SchemaVersion = manifest.SchemaVersion,
        Id = manifest.Id.Value,
        Name = manifest.Name,
        Version = manifest.Version,
        CapturedAt = manifest.CapturedAt,
        CapturedFromRunId = manifest.CapturedFromRunId,
        CaptureEndpoint = manifest.CaptureEndpoint.ToString(),
        CaptureEndpointLabel = manifest.CaptureEndpointLabel,
        PluginId = manifest.PluginId,
        ComparisonId = manifest.ComparisonId,
        PluginVersion = manifest.PluginVersion,
        Environment = manifest.EnvironmentName,
        ComparisonRulesSnapshotHash = manifest.ComparisonRulesSnapshotHash,
        Comparison = BaselineComparisonOptionsDto.FromOptions(manifest.ComparisonOptions),
        ToolVersion = manifest.ToolVersion,
        CapturedBy = manifest.CapturedBy,
        CapturedOnMachine = manifest.CapturedOnMachine,
        Scenarios = manifest.Scenarios.Select(BaselineScenarioEntryDto.FromEntry).ToList(),
    };

    public BaselinePackageManifest ToManifest() => new BaselinePackageManifest(
        new BaselineId(Id),
        Name,
        Version,
        CapturedAt,
        CapturedFromRunId,
        new Uri(CaptureEndpoint, UriKind.Absolute),
        PluginId,
        ComparisonId,
        PluginVersion,
        Environment,
        CaptureEndpointLabel,
        ComparisonRulesSnapshotHash,
        Comparison?.ToOptions(),
        ToolVersion,
        CapturedBy,
        CapturedOnMachine,
        Scenarios.Select(scenario => scenario.ToEntry()),
        SchemaVersion);
}

internal sealed class BaselineScenarioEntryDto
{
    public string RelativePath { get; set; } = string.Empty;

    public string RequestContentType { get; set; } = "text/plain";

    public long RequestContentLength { get; set; }

    public Dictionary<string, string> RequestHeaders { get; set; } = new Dictionary<string, string>();

    public int StatusCode { get; set; }

    public string? ResponseContentType { get; set; }

    public string CanonicalSha256 { get; set; } = string.Empty;

    public long CanonicalContentLength { get; set; }

    public string? RawSha256 { get; set; }

    public long RawContentLength { get; set; }

    public static BaselineScenarioEntryDto FromEntry(BaselineScenarioEntry entry) => new BaselineScenarioEntryDto
    {
        RelativePath = entry.RelativePath,
        RequestContentType = entry.RequestContentType,
        RequestContentLength = entry.RequestContentLength,
        RequestHeaders = entry.RequestHeaders.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase),
        StatusCode = entry.StatusCode,
        ResponseContentType = entry.ResponseContentType,
        CanonicalSha256 = entry.CanonicalSha256,
        CanonicalContentLength = entry.CanonicalContentLength,
        RawSha256 = entry.RawSha256,
        RawContentLength = entry.RawContentLength,
    };

    public BaselineScenarioEntry ToEntry() => new BaselineScenarioEntry(
        RelativePath,
        RequestContentType,
        RequestContentLength,
        StatusCode,
        ResponseContentType,
        CanonicalSha256,
        CanonicalContentLength,
        RawSha256,
        RawContentLength,
        RequestHeaders);
}

/// <summary>
/// The comparison settings a baseline was captured under, recorded so a later replay
/// can show whether it is being compared under the same rules.
/// </summary>
internal sealed class BaselineComparisonOptionsDto
{
    public bool IgnoreCollectionOrder { get; set; }

    public bool IgnoreStringCase { get; set; }

    public bool IgnoreTrailingWhitespaceAtEnd { get; set; }

    public bool TreatNullAndEmptyCollectionsAsEqual { get; set; }

    public bool IgnoreXmlNamespaces { get; set; } = true;

    public int MaxDifferences { get; set; } = 100;

    public List<IgnoreRuleDefinition> IgnoreRules { get; set; } = new List<IgnoreRuleDefinition>();

    public List<SmartIgnoreRuleDefinition> SmartIgnoreRules { get; set; } = new List<SmartIgnoreRuleDefinition>();

    public List<MaskRuleDefinition> MaskRules { get; set; } = new List<MaskRuleDefinition>();

    public static BaselineComparisonOptionsDto FromOptions(ComparisonOptions options) => new BaselineComparisonOptionsDto
    {
        IgnoreCollectionOrder = options.IgnoreCollectionOrder,
        IgnoreStringCase = options.IgnoreStringCase,
        IgnoreTrailingWhitespaceAtEnd = options.IgnoreTrailingWhitespaceAtEnd,
        TreatNullAndEmptyCollectionsAsEqual = options.TreatNullAndEmptyCollectionsAsEqual,
        IgnoreXmlNamespaces = options.IgnoreXmlNamespaces,
        MaxDifferences = options.MaxDifferences,
        IgnoreRules = options.IgnoreRules.ToList(),
        SmartIgnoreRules = options.SmartIgnoreRules.ToList(),
        MaskRules = options.MaskRules.ToList(),
    };

    public ComparisonOptions ToOptions() => new ComparisonOptions(
        IgnoreCollectionOrder,
        IgnoreStringCase,
        IgnoreTrailingWhitespaceAtEnd,
        TreatNullAndEmptyCollectionsAsEqual,
        IgnoreXmlNamespaces,
        MaxDifferences,
        IgnoreRules,
        SmartIgnoreRules,
        MaskRules);
}
