using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class LargeRunOptionsDto
{
    public int LargeRunThreshold { get; init; } = 1000;

    public int ChunkSize { get; init; } = 500;

    public int DetailPageSize { get; init; } = 250;

    public int? ComparisonConcurrency { get; init; }

    public int? MappingConcurrency { get; init; }

    public int? FocusedContentConcurrency { get; init; }

    public WorkerGcMode WorkerGcMode { get; init; }

    public int? ServerGcHeapCount { get; init; }

    public string? PerformanceCalibrationMachineFingerprint { get; init; }

    public int ProgressUpdateItemInterval { get; init; } = 100;

    public int ProgressUpdateMillisecondsInterval { get; init; } = 500;
}
