using ParityBench.NET.Domain.Runs;

namespace ParityBench.NET.Domain.Requests;

public sealed record RequestBatchManifest
{
    public RequestBatchManifest(
        RequestBatchReference batchReference,
        IEnumerable<RequestItem> requests,
        DateTimeOffset? createdAt = null)
    {
        ArgumentNullException.ThrowIfNull(requests);

        BatchReference = batchReference;
        Requests = requests.ToList();
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }

    public RequestBatchReference BatchReference { get; }

    public IReadOnlyList<RequestItem> Requests { get; }

    public DateTimeOffset CreatedAt { get; }
}
