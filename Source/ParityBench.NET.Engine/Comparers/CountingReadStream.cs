namespace ParityBench.NET.Engine.Comparers;

internal sealed class CountingReadStream : Stream
{
    private readonly Stream inner;
    private readonly Action<long> onRead;

    public CountingReadStream(Stream inner, Action<long> onRead)
    {
        this.inner = inner;
        this.onRead = onRead;
    }

    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => inner.CanSeek;
    public override bool CanWrite => false;
    public override long Length => inner.Length;
    public override long Position { get => inner.Position; set => inner.Position = value; }
    public override void Flush() => inner.Flush();
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);
    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));
    public override int Read(Span<byte> buffer) => Count(inner.Read(buffer));
    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Count(await inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false));
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Count(await inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false));
    public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Write(ReadOnlySpan<byte> buffer) => throw new NotSupportedException();
    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => throw new NotSupportedException();
    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing) { inner.Dispose(); }
        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }

    private int Count(int count)
    {
        if (count > 0) { onRead(count); }
        return count;
    }
}
