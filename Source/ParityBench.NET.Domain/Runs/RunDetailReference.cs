namespace ParityBench.NET.Domain.Runs;

public sealed record RunDetailReference
{
    public RunDetailReference(string detailId, ArtifactReference? artifact = null)
    {
        if (string.IsNullOrWhiteSpace(detailId))
        {
            throw new ArgumentException("Detail identifier must not be empty.", nameof(detailId));
        }

        DetailId = detailId;
        Artifact = artifact;
    }

    public string DetailId { get; }

    public ArtifactReference? Artifact { get; }
}
