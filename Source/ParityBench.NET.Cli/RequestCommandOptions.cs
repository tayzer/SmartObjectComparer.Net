using Microsoft.Extensions.Logging;

namespace ParityBench.NET.Cli;

public sealed record RequestCommandOptions(
    string? RequestDirectory,
    Uri? EndpointA,
    Uri? EndpointB,
    string? ModelName,
    string? ContractProfileId,
    int MaxConcurrency,
    TimeSpan Timeout,
    string? ContentTypeOverride,
    IReadOnlyList<string> CommonHeaders,
    IReadOnlyList<string> EndpointAHeaders,
    IReadOnlyList<string> EndpointBHeaders,
    string? ReportOutputDirectory,
    string? ReportAssetsDirectory,
    ObservabilityCliOptions Observability,
    string? PresetId = null);

public sealed record ObservabilityCliOptions(
    LogLevel? LogLevel = null,
    bool LogDurations = false,
    bool LogExceptions = false,
    bool PersistDiagnostics = false,
    int? SlowPathThresholdMs = null);