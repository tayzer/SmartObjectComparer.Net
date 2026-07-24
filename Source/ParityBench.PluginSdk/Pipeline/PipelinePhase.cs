namespace ParityBench.PluginSdk.Pipeline;

/// <summary>
/// The controlled phases a comparison run passes through. Steps are ordered by
/// phase first and can only be ordered relative to each other <em>within</em> a
/// phase, so a plugin cannot express an invalid pipeline (e.g. mapping before
/// transport).
/// </summary>
public enum PipelinePhase
{
    /// <summary>Load and inspect the source request for one endpoint slot.</summary>
    Input = 0,

    /// <summary>Build the outbound request: mapping, serialization, headers, auth.</summary>
    Request = 1,

    /// <summary>Send the request and capture the raw response.</summary>
    Transport = 2,

    /// <summary>Inspect, mask and persist the raw response.</summary>
    Response = 3,

    /// <summary>Project the response onto the comparison type.</summary>
    Mapping = 4,

    /// <summary>Diff the two comparison instances.</summary>
    Comparison = 5,

    /// <summary>Post-process the comparison result before it is persisted.</summary>
    ResultProcessing = 6,
}

/// <summary>
/// Classifies which context a phase runs against: <see cref="PipelinePhase.Input"/>
/// through <see cref="PipelinePhase.Mapping"/> run once per endpoint slot (A and B
/// concurrently); the remaining phases run once per request pair.
/// </summary>
public enum PipelineScope
{
    Endpoint = 0,
    Pair = 1,
}

public static class PipelinePhases
{
    /// <summary>
    /// Gets the scope a phase belongs to. Used by the pipeline builder to reject a
    /// step whose middleware kind does not match its declared phase.
    /// </summary>
    public static PipelineScope GetScope(this PipelinePhase phase) =>
        phase <= PipelinePhase.Mapping ? PipelineScope.Endpoint : PipelineScope.Pair;
}
