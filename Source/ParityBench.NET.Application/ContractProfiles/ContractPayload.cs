using ParityBench.NET.Domain.ContractProfiles;

namespace ParityBench.NET.Application.ContractProfiles;

public sealed class ContractPayload : IAsyncDisposable
{
    private readonly Func<CancellationToken, ValueTask<Stream>> openReadAsync;
    private readonly Func<ValueTask>? disposeAsync;
    private bool disposed;

    public ContractPayload(
        PayloadFormat format,
        string contentType,
        Func<CancellationToken, ValueTask<Stream>> openReadAsync,
        long? contentLength = null,
        Func<ValueTask>? disposeAsync = null)
    {
        ArgumentNullException.ThrowIfNull(openReadAsync);

        if (string.IsNullOrWhiteSpace(contentType))
        {
            throw new ArgumentException("Content type must not be empty.", nameof(contentType));
        }

        if (contentLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contentLength), "Content length must not be negative.");
        }

        Format = format;
        ContentType = contentType.Trim();
        ContentLength = contentLength;
        this.openReadAsync = openReadAsync;
        this.disposeAsync = disposeAsync;
    }

    public PayloadFormat Format { get; }

    public string ContentType { get; }

    public long? ContentLength { get; }

    public static ContractPayload FromBytes(
        byte[] content,
        PayloadFormat format,
        string contentType)
    {
        ArgumentNullException.ThrowIfNull(content);

        byte[] ownedContent = content.ToArray();
        return new ContractPayload(
            format,
            contentType,
            _ => ValueTask.FromResult<Stream>(new MemoryStream(ownedContent, writable: false)),
            ownedContent.Length);
    }

    public ValueTask<Stream> OpenReadAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellationToken.ThrowIfCancellationRequested();

        return openReadAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        if (disposeAsync is not null)
        {
            await disposeAsync().ConfigureAwait(false);
        }
    }
}
