namespace ParityBench.NET.Application.Requests;

public sealed class EndpointResponse : IAsyncDisposable
{
    private readonly IReadOnlyList<IDisposable> owners;

    public EndpointResponse(
        int statusCode,
        string? contentType,
        Stream body,
        IEnumerable<IDisposable>? owners = null,
        CancellationToken timeout = default)
    {
        ArgumentNullException.ThrowIfNull(body);

        StatusCode = statusCode;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType;
        Body = body;
        this.owners = owners is null ? Array.Empty<IDisposable>() : owners.ToList();
        Timeout = timeout;
    }

    public int StatusCode { get; }

    public string? ContentType { get; }

    public Stream Body { get; }

    /// <summary>
    /// Cancelled when the request's configured timeout elapses. Callers reading
    /// <see cref="Body"/> after headers arrive must link this token in, since the
    /// header read alone does not bound the time spent downloading the body.
    /// </summary>
    public CancellationToken Timeout { get; }

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync().ConfigureAwait(false);

        foreach (IDisposable owner in owners)
        {
            owner.Dispose();
        }
    }
}
