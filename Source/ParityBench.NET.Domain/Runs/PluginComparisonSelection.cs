namespace ParityBench.NET.Domain.Runs;

/// <summary>
/// What a run selected from the installed plugins. Everything here is a stable
/// logical id — never an assembly-qualified type name — so a saved profile keeps
/// working across plugin rebuilds and versions.
/// </summary>
public sealed record PluginComparisonSelection
{
    public PluginComparisonSelection(
        string pluginId,
        string comparisonId,
        string? pluginVersion = null,
        IEnumerable<string>? enabledStepIds = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? stepConfiguration = null,
        string? environmentName = null)
    {
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            throw new ArgumentException("Plugin id must not be empty.", nameof(pluginId));
        }

        if (string.IsNullOrWhiteSpace(comparisonId))
        {
            throw new ArgumentException("Comparison id must not be empty.", nameof(comparisonId));
        }

        PluginId = pluginId.Trim();
        ComparisonId = comparisonId.Trim();
        PluginVersion = string.IsNullOrWhiteSpace(pluginVersion) ? null : pluginVersion.Trim();
        EnabledStepIds = (enabledStepIds ?? Array.Empty<string>())
            .Where(stepId => !string.IsNullOrWhiteSpace(stepId))
            .Select(stepId => stepId.Trim())
            .ToArray();
        StepConfiguration = stepConfiguration;
        EnvironmentName = string.IsNullOrWhiteSpace(environmentName) ? null : environmentName.Trim();
    }

    public string PluginId { get; }

    public string ComparisonId { get; }

    /// <summary>
    /// Gets the plugin version the profile was authored against, if it pinned one.
    /// A null version means "whatever is installed".
    /// </summary>
    public string? PluginVersion { get; }

    /// <summary>
    /// Gets the optional pipeline steps this run enables on top of the ones the
    /// comparison enables by default.
    /// </summary>
    public IReadOnlyList<string> EnabledStepIds { get; }

    /// <summary>
    /// Gets per-step configuration, keyed by step id. Secret references are
    /// resolved to values before the pipeline is built.
    /// </summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? StepConfiguration { get; }

    public string? EnvironmentName { get; }
}
