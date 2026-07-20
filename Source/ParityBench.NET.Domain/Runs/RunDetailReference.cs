namespace ParityBench.NET.Domain.Runs;

public sealed record RunDetailReference
{
    public RunDetailReference(
        string detailId,
        ArtifactReference? artifact = null,
        int schemaVersion = 2,
        int pageSize = 250,
        int totalCount = 0,
        ArtifactReference? analysisArtifact = null,
        ArtifactReference? differenceIndexArtifact = null)
    {
        if (string.IsNullOrWhiteSpace(detailId))
        {
            throw new ArgumentException("Detail identifier must not be empty.", nameof(detailId));
        }

        if (schemaVersion <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(schemaVersion), "Schema version must be greater than zero.");
        }

        if (pageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be greater than zero.");
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Total count must not be negative.");
        }

        DetailId = detailId;
        Artifact = artifact;
        SchemaVersion = schemaVersion;
        PageSize = pageSize;
        TotalCount = totalCount;
        AnalysisArtifact = analysisArtifact;
        DifferenceIndexArtifact = differenceIndexArtifact;
    }

    public string DetailId { get; }

    public ArtifactReference? Artifact { get; }

    public int SchemaVersion { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public ArtifactReference? AnalysisArtifact { get; }

    public ArtifactReference? DifferenceIndexArtifact { get; }
}
