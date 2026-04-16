namespace ComparisonTool.Core.Serialization.BlazorReport;

/// <summary>
/// Represents display-ready raw content stored in a static report sidecar asset.
/// </summary>
public sealed class BundledRawContentData
{
    public string ContentA { get; set; } = string.Empty;

    public string ContentB { get; set; } = string.Empty;

    public bool IsTruncatedA { get; set; }

    public bool IsTruncatedB { get; set; }

    public string? ErrorMessage { get; set; }
}