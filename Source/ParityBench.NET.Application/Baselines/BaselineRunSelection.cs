using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Application.Baselines;

/// <summary>
/// What a host asked for on the way in, before the store has resolved it. A replay
/// may name no version, meaning "the latest one"; capture names no id, because the
/// store assigns the version when the run starts.
/// </summary>
public sealed record BaselineRunSelection
{
    private BaselineRunSelection(
        BaselineRunMode mode,
        string? captureName,
        BaselineId? baselineId,
        int? version,
        EndpointSlot baselineSlot)
    {
        Mode = mode;
        CaptureName = captureName;
        BaselineId = baselineId;
        Version = version;
        BaselineSlot = baselineSlot;
    }

    public BaselineRunMode Mode { get; }

    public string? CaptureName { get; }

    public BaselineId? BaselineId { get; }

    /// <summary>Gets the requested version, or null for the latest completed one.</summary>
    public int? Version { get; }

    public EndpointSlot BaselineSlot { get; }

    public static BaselineRunSelection Capture(string name, EndpointSlot baselineSlot = EndpointSlot.A)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Baseline capture name must not be empty.", nameof(name));
        }

        return new BaselineRunSelection(BaselineRunMode.CaptureBaseline, name.Trim(), null, null, baselineSlot);
    }

    public static BaselineRunSelection Replay(
        BaselineId id,
        int? version = null,
        EndpointSlot baselineSlot = EndpointSlot.A) =>
        new BaselineRunSelection(BaselineRunMode.BaselineVsLive, null, id, version, baselineSlot);

    /// <summary>
    /// Parses the CLI form <c>&lt;id&gt;</c> or <c>&lt;id&gt;@&lt;version&gt;</c>.
    /// </summary>
    public static BaselineRunSelection ParseReplay(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Baseline reference must not be empty.", nameof(value));
        }

        string trimmed = value.Trim();
        int separatorIndex = trimmed.LastIndexOf('@');
        if (separatorIndex < 0)
        {
            return Replay(new BaselineId(trimmed));
        }

        string idPart = trimmed[..separatorIndex];
        string versionPart = trimmed[(separatorIndex + 1)..].TrimStart('v', 'V');
        if (!int.TryParse(versionPart, out int version) || version <= 0)
        {
            throw new ArgumentException($"Baseline reference '{value}' has an invalid version.", nameof(value));
        }

        return Replay(new BaselineId(idPart), version);
    }
}
