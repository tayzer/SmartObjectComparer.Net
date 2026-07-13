using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

internal sealed class RequestBatchManifestDto
{
    public string BatchReference { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public List<RequestItemDto> Requests { get; init; } = new List<RequestItemDto>();
}
