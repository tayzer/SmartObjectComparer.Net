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

internal sealed class DetailPageInfoDto
{
    public int PageIndex { get; init; }

    public int Offset { get; init; }

    public int ItemCount { get; init; }

    public string Path { get; init; } = string.Empty;
}
