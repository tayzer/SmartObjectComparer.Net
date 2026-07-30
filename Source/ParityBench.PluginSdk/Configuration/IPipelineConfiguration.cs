namespace ParityBench.PluginSdk.Configuration;

/// <summary>
/// Profile-supplied configuration for a run, addressed by step id.
/// </summary>
public interface IPipelineConfiguration
{
    /// <summary>
    /// Gets the configuration block saved for a step. Returns an empty block when
    /// the profile configured nothing for it, so callers never null-check.
    /// </summary>
    IStepConfiguration ForStep(string stepId);

    /// <summary>Gets the name of the environment the run selected, if any.</summary>
    string? EnvironmentName { get; }
}

/// <summary>
/// One step's configuration block. Values marked as secrets in the step's schema
/// arrive already resolved from the secret store — a plugin never sees, and never
/// needs, the underlying <c>secret://</c> reference.
/// </summary>
public interface IStepConfiguration
{
    bool TryGetValue(string key, out string? value);

    string? GetString(string key, string? defaultValue = null);

    string GetRequiredString(string key);

    bool GetBoolean(string key, bool defaultValue = false);

    int GetInt32(string key, int defaultValue = 0);

    Uri GetRequiredUri(string key);
}
