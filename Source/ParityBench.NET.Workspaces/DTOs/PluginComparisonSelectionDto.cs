namespace ParityBench.NET.Workspaces;

/// <summary>
/// Persisted form of a run's plugin comparison selection. Stores stable logical
/// ids only, so a saved run keeps resolving after a plugin is rebuilt or upgraded.
/// </summary>
internal sealed class PluginComparisonSelectionDto
{
    public string PluginId { get; init; } = string.Empty;

    public string ComparisonId { get; init; } = string.Empty;

    public string? PluginVersion { get; init; }

    public string? EnvironmentName { get; init; }

    public List<string> EnabledStepIds { get; init; } = new List<string>();

    public Dictionary<string, Dictionary<string, string>> StepConfiguration { get; init; } = new Dictionary<string, Dictionary<string, string>>();
}
