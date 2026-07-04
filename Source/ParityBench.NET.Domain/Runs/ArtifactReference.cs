namespace ParityBench.NET.Domain.Runs;

public sealed record ArtifactReference
{
    public ArtifactReference(string artifactId, string? contentType = null)
    {
        if (string.IsNullOrWhiteSpace(artifactId))
        {
            throw new ArgumentException("Artifact identifier must not be empty.", nameof(artifactId));
        }

        ArtifactId = artifactId;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType;
    }

    public string ArtifactId { get; }

    public string? ContentType { get; }
}
