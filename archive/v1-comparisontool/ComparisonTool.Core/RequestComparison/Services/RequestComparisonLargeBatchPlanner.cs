using ComparisonTool.Core.RequestComparison.Models;

namespace ComparisonTool.Core.RequestComparison.Services;

/// <summary>
/// Centralizes deterministic large-batch thresholding and chunk partitioning.
/// </summary>
public static class RequestComparisonLargeBatchPlanner
{
    public static bool ShouldUseLargeBatchMode(int requestCount, RequestComparisonLargeBatchOptions options)
    {
        var threshold = NormalizeThreshold(options.LargeBatchThreshold);
        return requestCount >= threshold;
    }

    public static int GetEffectiveChunkSize(RequestComparisonLargeBatchOptions options) =>
        Math.Max(1, options.LargeBatchChunkSize);

    public static IReadOnlyList<IReadOnlyList<T>> Partition<T>(
        IReadOnlyList<T> items,
        int chunkSize)
    {
        if (items.Count == 0)
        {
            return Array.Empty<IReadOnlyList<T>>();
        }

        chunkSize = Math.Max(1, chunkSize);
        var chunks = new List<IReadOnlyList<T>>((int)Math.Ceiling(items.Count / (double)chunkSize));

        for (var index = 0; index < items.Count; index += chunkSize)
        {
            chunks.Add(items.Skip(index).Take(Math.Min(chunkSize, items.Count - index)).ToList());
        }

        return chunks;
    }

    private static int NormalizeThreshold(int threshold) => Math.Max(1, threshold);
}
