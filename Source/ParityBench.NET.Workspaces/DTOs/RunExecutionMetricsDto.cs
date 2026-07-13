using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunExecutionMetricsDto
{
    public double TotalDurationMilliseconds { get; init; }

    public double RequestExecutionDurationMilliseconds { get; init; }

    public double ComparisonDurationMilliseconds { get; init; }

    public double FinalizationDurationMilliseconds { get; init; }

    public int RequestCount { get; init; }

    public int MaxConcurrency { get; init; }

    public long ResponseBytesWritten { get; init; }

    public int RetainedArtifactCount { get; init; }

    public int TrimmedByPolicyArtifactCount { get; init; }

    public int MissingUnexpectedlyArtifactCount { get; init; }

    public double? CompareNormalizeDurationMilliseconds { get; init; }

    public double? ComparePersistCanonicalDurationMilliseconds { get; init; }

    public double? CompareDiffDurationMilliseconds { get; init; }

    public double? CompareFocusedContentDurationMilliseconds { get; init; }
}
