using System.Text.Json;
using System.Text.Json.Serialization;

using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Application.Runs.Worker;

/// <summary>
/// The newline-delimited JSON frames exchanged between the host and a run worker
/// process over the per-run named pipe.
/// </summary>
/// <remarks>
/// The frames are deliberately flat DTOs rather than the domain records: the wire
/// format is a contract between two processes and must stay stable independently of
/// the in-memory model, and flat DTOs round-trip through
/// <see cref="System.Text.Json"/> without depending on constructor-parameter names.
/// </remarks>
public static class WorkerProtocol
{
    public static JsonSerializerOptions JsonOptions { get; } = new JsonSerializerOptions
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>The kind of a host→worker frame.</summary>
    public enum HostFrameKind
    {
        Cancel = 0,
    }

    /// <summary>The kind of a worker→host frame.</summary>
    public enum WorkerFrameKind
    {
        Progress = 0,
        Summary = 1,
        Error = 2,
    }

    public sealed record HostFrame(HostFrameKind Kind);

    public sealed record WorkerFrame(
        WorkerFrameKind Kind,
        ProgressPayload? Progress = null,
        RunResultSummary? Summary = null,
        string? ErrorMessage = null);

    public sealed record ProgressPayload(
        RunStatus Status,
        int Percent,
        string Message,
        int? CompletedRequests,
        int? TotalRequests,
        bool Force)
    {
        public static ProgressPayload From(RunStatus status, RunProgress progress, bool force) => new ProgressPayload(
            status,
            progress.PercentComplete,
            progress.Message,
            progress.CompletedItems,
            progress.TotalItems,
            force);

        public RunProgress ToProgress() => new RunProgress(Percent, Message, CompletedRequests, TotalRequests);
    }

    public static string Serialize<T>(T frame) => JsonSerializer.Serialize(frame, JsonOptions);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, JsonOptions);
}
