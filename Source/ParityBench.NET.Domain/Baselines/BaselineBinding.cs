using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// What a run does with baselines: nothing, capture one, or replay one as the
/// expected side. Carried on <see cref="Runs.RunOptions"/> so it is persisted with
/// the run and travels to the out-of-process worker unchanged.
/// </summary>
public sealed record BaselineBinding
{
    private BaselineBinding(
        BaselineRunMode mode,
        BaselineId? baselineId,
        int? version,
        EndpointSlot baselineSlot,
        string? captureName)
    {
        Mode = mode;
        BaselineId = baselineId;
        Version = version;
        BaselineSlot = baselineSlot;
        CaptureName = captureName;
    }

    public BaselineRunMode Mode { get; }

    /// <summary>Gets the baseline being replayed, or the one capture wrote to once the run has started.</summary>
    public BaselineId? BaselineId { get; }

    /// <summary>Gets the package version being replayed. Null while capturing — the store assigns it.</summary>
    public int? Version { get; }

    /// <summary>
    /// Gets the slot the baseline occupies. Capture reads from it; replay supplies it
    /// from storage instead of calling it.
    /// </summary>
    public EndpointSlot BaselineSlot { get; }

    /// <summary>Gets the display name a capture run saves under.</summary>
    public string? CaptureName { get; }

    public bool IsBaselineSlot(EndpointSlot endpoint) =>
        Mode != BaselineRunMode.LiveVsLive && endpoint == BaselineSlot;

    public static BaselineBinding ForCapture(string captureName, EndpointSlot baselineSlot = EndpointSlot.A)
    {
        if (string.IsNullOrWhiteSpace(captureName))
        {
            throw new ArgumentException("Baseline capture name must not be empty.", nameof(captureName));
        }

        return new BaselineBinding(
            BaselineRunMode.CaptureBaseline,
            null,
            null,
            baselineSlot,
            captureName.Trim());
    }

    public static BaselineBinding ForReplay(
        BaselineId baselineId,
        int version,
        EndpointSlot baselineSlot = EndpointSlot.A)
    {
        if (version <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(version), "Baseline version must be greater than zero.");
        }

        return new BaselineBinding(
            BaselineRunMode.BaselineVsLive,
            baselineId,
            version,
            baselineSlot,
            null);
    }
}
