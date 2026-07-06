namespace ParityBench.NET.Application.Workflow;

public sealed class InMemoryRequestComparisonPresetRegistry : IRequestComparisonPresetRegistry
{
    private readonly Dictionary<string, RequestComparisonPresetOption> presetsById =
        new Dictionary<string, RequestComparisonPresetOption>(StringComparer.OrdinalIgnoreCase);
    private readonly object gate = new object();

    public void Register(RequestComparisonPresetOption preset)
    {
        ArgumentNullException.ThrowIfNull(preset);

        if (string.IsNullOrWhiteSpace(preset.PresetId))
        {
            throw new ArgumentException("Preset id must not be empty.", nameof(preset));
        }

        lock (gate)
        {
            if (presetsById.ContainsKey(preset.PresetId))
            {
                throw new InvalidOperationException($"Preset '{preset.PresetId}' is already registered.");
            }

            presetsById[preset.PresetId] = preset;
        }
    }

    public IReadOnlyList<RequestComparisonPresetOption> ListPresets()
    {
        lock (gate)
        {
            return presetsById.Values
                .OrderBy(preset => preset.Label, StringComparer.OrdinalIgnoreCase)
                .ThenBy(preset => preset.PresetId, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }
}
