using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Results;

public sealed record ArtifactContentPreview
{
    public ArtifactContentPreview(
        ArtifactReference artifact,
        string content,
        int bytesRead,
        bool isTruncated,
        string? contentType = null,
        long? totalLength = null)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (bytesRead < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesRead), "Bytes read must not be negative.");
        }

        if (totalLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalLength), "Total length must not be negative.");
        }

        Artifact = artifact;
        Content = content ?? string.Empty;
        BytesRead = bytesRead;
        IsTruncated = isTruncated;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? artifact.ContentType : contentType;
        TotalLength = totalLength;
    }

    public ArtifactReference Artifact { get; }

    public string Content { get; }

    public int BytesRead { get; }

    public bool IsTruncated { get; }

    public string? ContentType { get; }

    public long? TotalLength { get; }
}