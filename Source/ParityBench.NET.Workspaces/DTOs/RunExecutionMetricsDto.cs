using System.Text.Json;
using System.Text.Json.Serialization;
using ParityBench.NET.Application.Runs;
using ParityBench.NET.Domain.ContractProfiles;
using ParityBench.NET.Domain.Comparison;
using ParityBench.NET.Domain.Requests;
using ParityBench.NET.Domain.Runs;
using ParityBench.NET.Domain.Runs.Retention;

namespace ParityBench.NET.Workspaces;

internal sealed class RunExecutionMetricsDto
{
    public double TotalDurationMilliseconds { get; init; }

    public double RequestExecutionDurationMilliseconds { get; init; }

    public double ComparisonDurationMilliseconds { get; init; }

    public double FinalizationDurationMilliseconds { get; init; }

    public int RequestCount { get; init; }

    public int MaxConcurrency { get; init; }

    public long ResponseBytesWritten { get; init; }

    public int ComparisonConcurrency { get; init; }

    public int RetainedArtifactCount { get; init; }

    public int TrimmedByPolicyArtifactCount { get; init; }

    public int MissingUnexpectedlyArtifactCount { get; init; }

    public double? CompareNormalizeDurationMilliseconds { get; init; }

    public double? ComparePersistCanonicalDurationMilliseconds { get; init; }

    public double? CompareDiffDurationMilliseconds { get; init; }

    public double? CompareFocusedContentDurationMilliseconds { get; init; }

    public DetailedCompareMetricsDto? DetailedCompareMetrics { get; init; }

    public RunProcessResourceMetricsDto? ProcessResourceMetrics { get; init; }
}

internal sealed class DetailedCompareMetricsDto
{
    public double ArtifactOpenDurationMilliseconds { get; init; }
    public long ArtifactBytesRead { get; init; }
    public double ResponseDeserializationDurationMilliseconds { get; init; }
    public double ComparisonModelNormalizationDurationMilliseconds { get; init; }
    public double CompareNetObjectsTraversalDurationMilliseconds { get; init; }
    public double DifferenceMaterializationDurationMilliseconds { get; init; }
    public double CanonicalMappingDurationMilliseconds { get; init; }
    public double PluginMappingDurationMilliseconds { get; init; }
    public double PluginPairProcessingDurationMilliseconds { get; init; }
    public double FocusedContentDurationMilliseconds { get; init; }
    public double OtherCompareWorkerDurationMilliseconds { get; init; }
    public double CompareQueueWaitDurationMilliseconds { get; init; }
    public double ExecutionWorkerBackpressureDurationMilliseconds { get; init; }
}

internal sealed class RunProcessResourceMetricsDto
{
    public double ProcessCpuDurationMilliseconds { get; init; }
    public double AverageProcessCoreUtilizationPercent { get; init; }
    public double AverageMachineCpuUtilizationPercent { get; init; }
    public long PeakWorkingSetBytes { get; init; }
    public long PeakPrivateBytes { get; init; }
    public long ManagedAllocatedBytes { get; init; }
    public int Gen0CollectionCount { get; init; }
    public int Gen1CollectionCount { get; init; }
    public int Gen2CollectionCount { get; init; }
    public int LogicalProcessorCount { get; init; }
}
