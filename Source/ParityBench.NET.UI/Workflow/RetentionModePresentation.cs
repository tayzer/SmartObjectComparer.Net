using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.UI.Workflow;

/// <summary>
/// How the retention modes are named in the UI. The enum names say what is trimmed;
/// an operator picking a mode is usually asking the opposite question — what do I
/// still get to look at afterwards — so the labels answer that.
/// </summary>
/// <remarks>
/// Shared by the run workflow's per-run picker and the profile editor's default, so
/// the same mode never reads as two different things in two places.
/// </remarks>
public static class RetentionModePresentation
{
    public static IReadOnlyList<RetentionMode> Modes { get; } = Enum.GetValues<RetentionMode>();

    public static string Label(RetentionMode mode) => mode switch
    {
        RetentionMode.None => "Keep everything — full raw responses for every pair",
        RetentionMode.TrimmedEquals => "Trim equal pairs — full raw responses wherever the pair differed",
        RetentionMode.TrimmedIgnoredPaths => "Trim ignored paths — keep equal pairs, focused content for differences",
        RetentionMode.TrimmedEqualsAndIgnoredPaths => "Trim equal pairs and ignored paths (default)",
        _ => mode.ToString(),
    };

    /// <summary>
    /// Parses a picker value, where empty means "no override — use the configured
    /// default" rather than a mode.
    /// </summary>
    public static RetentionMode? Parse(string? value) =>
        Enum.TryParse(value, out RetentionMode mode) && Enum.IsDefined(mode) ? mode : null;
}
