using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunDetailReferenceDto
{
    public string DetailId { get; init; } = string.Empty;

    public ArtifactReferenceDto? Artifact { get; init; }

    public int SchemaVersion { get; init; } = 1;

    public int PageSize { get; init; } = 250;

    public int TotalCount { get; init; }

    public ArtifactReferenceDto? AnalysisArtifact { get; init; }

    public ArtifactReferenceDto? DifferenceIndexArtifact { get; init; }
}
