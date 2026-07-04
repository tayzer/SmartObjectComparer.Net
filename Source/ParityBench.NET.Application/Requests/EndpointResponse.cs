namespace ParityBench.NET.Application.Requests;

public sealed class EndpointResponse : IAsyncDisposable
{
    private readonly IReadOnlyList<IDisposable> owners;

    public EndpointResponse(
        int statusCode,
        string? contentType,
        Stream body,
        IEnumerable<IDisposable>? owners = null)
    {
        ArgumentNullException.ThrowIfNull(body);

        StatusCode = statusCode;
        ContentType = string.IsNullOrWhiteSpace(contentType) ? null : contentType;
        Body = body;
        this.owners = owners is null ? Array.Empty<IDisposable>() : owners.ToList();
    }

    public int StatusCode { get; }

    public string? ContentType { get; }

    public Stream Body { get; }

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync().ConfigureAwait(false);

        foreach (IDisposable owner in owners)
        {
            owner.Dispose();
        }
    }
}
