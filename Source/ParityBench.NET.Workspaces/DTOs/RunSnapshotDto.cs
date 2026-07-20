using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunSnapshotDto
{
    public string Id { get; init; } = string.Empty;

    public RunOptionsDto Options { get; init; } = new RunOptionsDto();

    public RunStatus Status { get; init; }

    public RunProgressDto Progress { get; init; } = new RunProgressDto();

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset UpdatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }

    public RunResultSummaryDto? Summary { get; init; }

    public string? ErrorMessage { get; init; }

    public RunDiagnosticsSnapshotDto? Diagnostics { get; init; }

    public RetentionMode RunRetentionMode { get; init; } = RetentionMode.TrimmedEqualsAndIgnoredPaths;

    public string RunRetentionPolicyVersion { get; init; } = "v1";

    public string? ComparisonRulesSnapshotHash { get; init; }
}
