using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportDetailPage
{
    public StaticReportDetailPage(
        int pageIndex,
        int offset,
        int totalCount,
        IReadOnlyList<RequestPairResult>? items = null)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index must not be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
        }

        if (totalCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCount), "Total count must not be negative.");
        }

        PageIndex = pageIndex;
        Offset = offset;
        TotalCount = totalCount;
        Items = (items ?? Array.Empty<RequestPairResult>()).ToList();
    }

    public int PageIndex { get; }

    public int Offset { get; }

    public int TotalCount { get; }

    public IReadOnlyList<RequestPairResult> Items { get; }
}
