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

internal sealed class PairArtifactRetentionStateDto
{
    public ArtifactRetentionState RawResponseA { get; init; } = ArtifactRetentionState.Retained;

    public ArtifactRetentionState RawResponseB { get; init; } = ArtifactRetentionState.Retained;

    public ArtifactRetentionState CanonicalResponseA { get; init; } = ArtifactRetentionState.Retained;

    public ArtifactRetentionState CanonicalResponseB { get; init; } = ArtifactRetentionState.Retained;

    public ArtifactRetentionState FocusedResponseA { get; init; } = ArtifactRetentionState.Retained;

    public ArtifactRetentionState FocusedResponseB { get; init; } = ArtifactRetentionState.Retained;
}
