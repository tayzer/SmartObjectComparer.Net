using ParityBench.NET.Domain.Requests;

namespace ParityBench.NET.Domain.Results;

public sealed record RunDetailQuery
{
    public const int DefaultLimit = 100;

    public const int MaxLimit = 500;

    public RunDetailQuery(
        int offset = 0,
        int limit = DefaultLimit,
        RequestPairOutcome? outcome = null,
        string? relativePathSearch = null)
    {
        if (offset < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(offset), "Offset must not be negative.");
        }

        if (limit is < 1 or > MaxLimit)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), $"Limit must be between 1 and {MaxLimit}.");
        }

        Offset = offset;
        Limit = limit;
        Outcome = outcome;
        RelativePathSearch = NormalizeSearch(relativePathSearch);
    }

    public int Offset { get; }

    public int Limit { get; }

    public RequestPairOutcome? Outcome { get; }

    public string? RelativePathSearch { get; }

    private static string? NormalizeSearch(string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return null;
        }

        return search.Trim().Replace('\\', '/');
    }
}