namespace ParityBench.NET.Application.Runs;

/// <summary>Describes a run snapshot that could not be read safely.</summary>
public sealed record RunSnapshotRecoveryWarning(
    string SnapshotPath,
    string? QuarantinedPath,
    string Message);
