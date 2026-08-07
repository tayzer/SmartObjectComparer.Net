namespace ParityBench.NET.Domain.Baselines;

/// <summary>
/// How a run sources the two sides of its comparison.
/// </summary>
public enum BaselineRunMode
{
    /// <summary>Both endpoints are called live. The original and default mode.</summary>
    LiveVsLive = 0,

    /// <summary>One endpoint is called live and its scenarios are recorded into a baseline package.</summary>
    CaptureBaseline = 1,

    /// <summary>The expected side is replayed from a captured baseline; only the live side is called.</summary>
    BaselineVsLive = 2,
}
