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

internal sealed class ComparisonDifferenceDto
{
    public string PropertyPath { get; init; } = string.Empty;

    public string? ValueA { get; init; }

    public string? ValueB { get; init; }

    public string? Message { get; init; }
}
