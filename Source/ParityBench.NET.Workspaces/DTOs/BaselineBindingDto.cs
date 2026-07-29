using ParityBench.NET.Domain.Baselines;
using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Workspaces;

/// <summary>
/// Persisted form of a run's baseline binding. Absent on runs created before the
/// feature existed and on every plain live-vs-live run.
/// </summary>
internal sealed class BaselineBindingDto
{
    public BaselineRunMode Mode { get; init; } = BaselineRunMode.LiveVsLive;

    public string? BaselineId { get; init; }

    public int? Version { get; init; }

    public EndpointSlot BaselineSlot { get; init; } = EndpointSlot.A;

    public string? CaptureName { get; init; }

    public static BaselineBindingDto? FromBinding(BaselineBinding? binding) =>
        binding is null
            ? null
            : new BaselineBindingDto
            {
                Mode = binding.Mode,
                BaselineId = binding.BaselineId?.Value,
                Version = binding.Version,
                BaselineSlot = binding.BaselineSlot,
                CaptureName = binding.CaptureName,
            };

    public BaselineBinding? ToBinding() => Mode switch
    {
        BaselineRunMode.CaptureBaseline when !string.IsNullOrWhiteSpace(CaptureName) =>
            BaselineBinding.ForCapture(CaptureName!, BaselineSlot),
        BaselineRunMode.BaselineVsLive when !string.IsNullOrWhiteSpace(BaselineId) && Version is > 0 =>
            BaselineBinding.ForReplay(new BaselineId(BaselineId!), Version!.Value, BaselineSlot),
        _ => null,
    };
}
