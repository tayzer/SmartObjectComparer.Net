namespace ParityBench.NET.Domain.Reports;

public sealed record StaticReportDetailPageInfo
{
    public StaticReportDetailPageInfo(
        int pageIndex,
        int offset,
        int itemCount,
        string path)
    {
        if (pageIndex < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(pageIndex), "Page index must not be negative.");
        }

        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
        }

        if (itemCount < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(itemCount), "Item count must not be negative.");
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("Page path must not be empty.", nameof(path));
        }

        PageIndex = pageIndex;
        Offset = offset;
        ItemCount = itemCount;
        Path = path.Replace('\\', '/');
    }

    public int PageIndex { get; }

    public int Offset { get; }

    public int ItemCount { get; }

    public string Path { get; }
}
