namespace ParityBench.NET.Domain.Runs;

public sealed record LargeRunOptions
{
    public LargeRunOptions(
        int largeRunThreshold = 1000,
        int chunkSize = 500,
        int detailPageSize = 250,
        int? comparisonConcurrency = null,
        int progressUpdateItemInterval = 100,
        int progressUpdateMillisecondsInterval = 500)
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
        ProgressUpdateItemInterval = progressUpdateItemInterval;
        ProgressUpdateMillisecondsInterval = progressUpdateMillisecondsInterval;
    }

    public int LargeRunThreshold { get; }

    public int ChunkSize { get; }

    public int DetailPageSize { get; }

    public int? ComparisonConcurrency { get; }

    public int ProgressUpdateItemInterval { get; }

    public int ProgressUpdateMillisecondsInterval { get; }
}
