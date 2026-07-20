using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Results;

public sealed class ArtifactNotFoundException : Exception
{
    public ArtifactNotFoundException(ArtifactReference artifact, Exception? innerException = null)
        : base($"Artifact '{artifact.ArtifactId}' was not found.", innerException)
    {
        Artifact = artifact;
    }

    public ArtifactReference Artifact { get; }
}