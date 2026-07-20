using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.InteropServices;
using ParityBench.NET.Application.Requests;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Workspaces;

internal sealed class RequestItemDto
{
    public string RelativePath { get; init; } = string.Empty;

    public string ContentType { get; init; } = "text/plain";

    public long ContentLength { get; init; }

    public Dictionary<string, string> Headers { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> HeadersA { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, string> HeadersB { get; init; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
