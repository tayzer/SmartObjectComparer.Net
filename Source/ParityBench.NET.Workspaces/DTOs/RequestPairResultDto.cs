using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Reports;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Results;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RequestPairResultDto
{
    public string RelativePath { get; init; } = string.Empty;

    public RequestPairOutcome Outcome { get; init; }

    public ResponseArtifactMetadataDto? ResponseA { get; init; }

    public ResponseArtifactMetadataDto? ResponseB { get; init; }

    public ResponseArtifactMetadataDto? FocusedResponseA { get; init; }

    public ResponseArtifactMetadataDto? FocusedResponseB { get; init; }

    public List<string> FocusedRawContentIgnorePaths { get; init; } = new List<string>();

    public string? ErrorMessage { get; init; }

    public string? OutcomeMessage { get; init; }

    public bool? AreEqual { get; init; }

    public int? DifferenceCount { get; init; }

    public List<ComparisonDifferenceDto> Differences { get; init; } = new List<ComparisonDifferenceDto>();

    public PairRetentionClass? PairRetentionClass { get; init; }

    public PairArtifactRetentionStateDto? ArtifactRetentionState { get; init; }

    public DateTimeOffset? RetentionAppliedAt { get; init; }
}
