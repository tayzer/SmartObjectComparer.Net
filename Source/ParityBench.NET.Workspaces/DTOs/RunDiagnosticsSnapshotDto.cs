using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunDiagnosticsSnapshotDto
{
    public List<SlowRequestPathDiagnosticDto> SlowRequestPaths { get; init; } = new List<SlowRequestPathDiagnosticDto>();

    public List<ExceptionDiagnosticDto> Exceptions { get; init; } = new List<ExceptionDiagnosticDto>();
}
