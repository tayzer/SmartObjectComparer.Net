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

    public PipelineStageMetricsDto? PipelineStageMetrics { get; init; }

    public NormalizationWorkMetricsDto? NormalizationWorkMetrics { get; init; }

    public RunRuntimeMetricsDto? RuntimeMetrics { get; init; }
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

internal sealed class PipelineStageMetricsDto
{
    public int MappingConcurrency { get; init; }
    public int ComparisonConcurrency { get; init; }
    public int FocusedContentConcurrency { get; init; }
    public int ExecuteToMappingCapacity { get; init; }
    public int MappingToComparisonCapacity { get; init; }
    public int ComparisonToFocusedCapacity { get; init; }
    public double MappingWorkerDurationMilliseconds { get; init; }
    public double ComparisonWorkerDurationMilliseconds { get; init; }
    public double FocusedContentWorkerDurationMilliseconds { get; init; }
    public double DetailPersistenceDurationMilliseconds { get; init; }
    public double ExecuteToMappingQueueWaitDurationMilliseconds { get; init; }
    public double MappingToComparisonQueueWaitDurationMilliseconds { get; init; }
    public double ComparisonToFocusedQueueWaitDurationMilliseconds { get; init; }
    public double ExecutionBackpressureDurationMilliseconds { get; init; }
    public double MappingBackpressureDurationMilliseconds { get; init; }
    public double ComparisonBackpressureDurationMilliseconds { get; init; }
    public int MaximumExecuteToMappingDepth { get; init; }
    public int MaximumMappingToComparisonDepth { get; init; }
    public int MaximumComparisonToFocusedDepth { get; init; }
}

internal sealed class NormalizationWorkMetricsDto
{
    public double GraphTraversalDurationMilliseconds { get; init; }
    public double SortKeyConstructionDurationMilliseconds { get; init; }
    public double CollectionSortDurationMilliseconds { get; init; }
    public double LegacyFallbackDurationMilliseconds { get; init; }
    public double RestorationDurationMilliseconds { get; init; }
    public long ObjectNodeCount { get; init; }
    public long PropertyNodeCount { get; init; }
    public long CollectionNodeCount { get; init; }
    public long CollectionItemCount { get; init; }
    public long ScalarNodeCount { get; init; }
    public long ScalarUtf8Bytes { get; init; }
    public long IgnoredNodeCount { get; init; }
    public long SortKeyBytes { get; init; }
    public long MaximumSortKeyBytes { get; init; }
    public long SortCollisionGroupCount { get; init; }
    public long MutableBranchCount { get; init; }
    public long LegacyFallbackBranchCount { get; init; }
}

internal sealed class RunRuntimeMetricsDto
{
    public bool IsServerGc { get; init; }
    public int? ConfiguredServerGcHeapCount { get; init; }
    public bool? DynamicAdaptationEnabled { get; init; }
    public long TotalAvailableMemoryBytes { get; init; }
    public long MemoryBudgetBytes { get; init; }
}
