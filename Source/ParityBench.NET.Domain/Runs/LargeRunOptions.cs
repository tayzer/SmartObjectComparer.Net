namespace ParityBench.NET.Domain.Runs;

public sealed record LargeRunOptions
{
    public LargeRunOptions(
        int largeRunThreshold = 1000,
        int chunkSize = 500,
        int detailPageSize = 250,
        int? comparisonConcurrency = null,
        int progressUpdateItemInterval = 100,
        int progressUpdateMillisecondsInterval = 500,
        int? mappingConcurrency = null,
        int? focusedContentConcurrency = null,
        WorkerGcMode workerGcMode = WorkerGcMode.Auto,
        int? serverGcHeapCount = null,
        string? performanceCalibrationMachineFingerprint = null)
    {
        if (largeRunThreshold <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(largeRunThreshold), "Large-run threshold must be greater than zero.");
        }

        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than zero.");
        }

        if (detailPageSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(detailPageSize), "Detail page size must be greater than zero.");
        }

        if (comparisonConcurrency is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(comparisonConcurrency), "Comparison concurrency must be greater than zero when supplied.");
        }

        if (mappingConcurrency is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(mappingConcurrency), "Mapping concurrency must be greater than zero when supplied.");
        }

        if (focusedContentConcurrency is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(focusedContentConcurrency), "Focused-content concurrency must be greater than zero when supplied.");
        }

        if (!Enum.IsDefined(workerGcMode))
        {
            throw new ArgumentOutOfRangeException(nameof(workerGcMode), "Worker GC mode is not supported.");
        }

        if (serverGcHeapCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(serverGcHeapCount), "Server GC heap count must be greater than zero when supplied.");
        }

        if (workerGcMode == WorkerGcMode.ServerFixed && serverGcHeapCount is null)
        {
            throw new ArgumentException("ServerFixed GC mode requires a server GC heap count.", nameof(serverGcHeapCount));
        }

        if (workerGcMode != WorkerGcMode.ServerFixed && serverGcHeapCount is not null)
        {
            throw new ArgumentException("Server GC heap count is valid only with ServerFixed GC mode.", nameof(serverGcHeapCount));
        }

        if (progressUpdateItemInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(progressUpdateItemInterval), "Progress item interval must be greater than zero.");
        }

        if (progressUpdateMillisecondsInterval <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(progressUpdateMillisecondsInterval), "Progress time interval must be greater than zero.");
        }

        LargeRunThreshold = largeRunThreshold;
        ChunkSize = chunkSize;
        DetailPageSize = detailPageSize;
        ComparisonConcurrency = comparisonConcurrency;
        MappingConcurrency = mappingConcurrency;
        FocusedContentConcurrency = focusedContentConcurrency;
        WorkerGcMode = workerGcMode;
        ServerGcHeapCount = serverGcHeapCount;
        PerformanceCalibrationMachineFingerprint = string.IsNullOrWhiteSpace(performanceCalibrationMachineFingerprint)
            ? null
            : performanceCalibrationMachineFingerprint.Trim();
        ProgressUpdateItemInterval = progressUpdateItemInterval;
        ProgressUpdateMillisecondsInterval = progressUpdateMillisecondsInterval;
    }

    public int LargeRunThreshold { get; }

    public int ChunkSize { get; }

    public int DetailPageSize { get; }

    public int? ComparisonConcurrency { get; }

    public int? MappingConcurrency { get; }

    public int? FocusedContentConcurrency { get; }

    public WorkerGcMode WorkerGcMode { get; }

    public int? ServerGcHeapCount { get; }

    public string? PerformanceCalibrationMachineFingerprint { get; }

    public int ProgressUpdateItemInterval { get; }

    public int ProgressUpdateMillisecondsInterval { get; }
}

public enum WorkerGcMode
{
    Auto = 0,
    Workstation = 1,
    ServerAdaptive = 2,
    ServerFixed = 3,
}
