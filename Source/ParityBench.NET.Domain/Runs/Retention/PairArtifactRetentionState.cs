namespace ParityBench.NET.Domain.Runs.Retention;

public sealed record PairArtifactRetentionState
{
    public PairArtifactRetentionState(
        ArtifactRetentionState rawResponseA = ArtifactRetentionState.Retained,
        ArtifactRetentionState rawResponseB = ArtifactRetentionState.Retained,
        ArtifactRetentionState canonicalResponseA = ArtifactRetentionState.Retained,
        ArtifactRetentionState canonicalResponseB = ArtifactRetentionState.Retained,
        ArtifactRetentionState focusedResponseA = ArtifactRetentionState.Retained,
        ArtifactRetentionState focusedResponseB = ArtifactRetentionState.Retained)
    {
        RawResponseA = rawResponseA;
        RawResponseB = rawResponseB;
        CanonicalResponseA = canonicalResponseA;
        CanonicalResponseB = canonicalResponseB;
        FocusedResponseA = focusedResponseA;
        FocusedResponseB = focusedResponseB;
    }

    public ArtifactRetentionState RawResponseA { get; }

    public ArtifactRetentionState RawResponseB { get; }

    public ArtifactRetentionState CanonicalResponseA { get; }

    public ArtifactRetentionState CanonicalResponseB { get; }

    public ArtifactRetentionState FocusedResponseA { get; }

    public ArtifactRetentionState FocusedResponseB { get; }

    public static PairArtifactRetentionState CreateDefaultRetained() => new PairArtifactRetentionState();
}