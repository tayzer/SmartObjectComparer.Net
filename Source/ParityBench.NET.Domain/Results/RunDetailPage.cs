using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Results;

public sealed record RunDetailPage
{
    public RunDetailPage(
        IReadOnlyList<RequestPairResult> items,
        int totalCount,
        int offset,
        int limit)
    {
        ArgumentNullException.ThrowIfNull(items);

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Total count must not be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
        }

        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be positive.");
        }

        if (items.Count > limit)
        {
            throw new ArgumentException("Page item count cannot exceed the page limit.", nameof(items));
        }

        Items = items;
        TotalCount = totalCount;
        Offset = offset;
        Limit = limit;
    }

    public IReadOnlyList<RequestPairResult> Items { get; }

    public int TotalCount { get; }

    public int Offset { get; }

    public int Limit { get; }

    public bool HasMore => Offset + Items.Count < TotalCount;
}