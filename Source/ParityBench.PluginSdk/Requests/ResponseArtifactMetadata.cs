using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Requests;

public sealed record ResponseArtifactMetadata
{
    public ResponseArtifactMetadata(
        EndpointSlot endpoint,
        ArtifactReference artifact,
        int statusCode,
        string? contentType,
        long contentLength,
        string sha256)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentLength), "Content length must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(sha256))
        {
            throw new ArgumentException("SHA-256 hash must not be empty.", nameof(sha256));
        }

        Endpoint = endpoint;
        Artifact = artifact;
        StatusCode = statusCode;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType;
        ContentLength = contentLength;
        Sha256 = sha256;
    }

    public EndpointSlot Endpoint { get; }

    public ArtifactReference Artifact { get; }

    public int StatusCode { get; }

    public string? ContentType { get; }

    public long ContentLength { get; }

    public string Sha256 { get; }
}
