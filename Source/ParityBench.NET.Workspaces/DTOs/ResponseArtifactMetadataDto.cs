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

internal sealed class ResponseArtifactMetadataDto
{
    public EndpointSlot Endpoint { get; init; }

    public string ArtifactId { get; init; } = string.Empty;

    public string? ArtifactContentType { get; init; }

    public int StatusCode { get; init; }

    public string? ContentType { get; init; }

    public long ContentLength { get; init; }

    public string Sha256 { get; init; } = string.Empty;
}
