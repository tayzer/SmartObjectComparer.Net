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

internal sealed class DetailManifestDto
{
    public int SchemaVersion { get; init; } = FileSystemRunDetailStore.CurrentSchemaVersion;

    public string RunId { get; init; } = string.Empty;

    public int PageSize { get; init; } = 250;

    public int TotalCount { get; init; }

    public string? AnalysisPath { get; init; }

    public string? DifferenceIndexPath { get; init; }

    public List<DetailPageInfoDto> Pages { get; init; } = new List<DetailPageInfoDto>();
}
