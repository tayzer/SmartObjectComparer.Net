namespace ParityBench.NET.Application.Workflow;

/// <summary>
/// Registers selectable example presets for request-comparison hosts.
/// </summary>
public interface IRequestComparisonPresetRegistry
{
    /// <summary>
    /// Registers an example preset.
    /// </summary>
    void Register(RequestComparisonPresetOption preset);

    /// <summary>
    /// Lists all registered presets.
    /// </summary>
    IReadOnlyList<RequestComparisonPresetOption> ListPresets();
}
