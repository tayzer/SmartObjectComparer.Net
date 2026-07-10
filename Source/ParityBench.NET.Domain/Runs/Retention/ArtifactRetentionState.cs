namespace ParityBench.NET.Domain.Runs.Retention;

public enum ArtifactRetentionState
{
    Retained,
    TrimmedByPolicy,
    MissingUnexpectedly,
}