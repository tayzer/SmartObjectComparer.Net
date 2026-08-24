using System.Buffers;

namespace ParityBench.NET.Engine.Comparers;

internal sealed class BoundedByteArrayPool
{
    internal static int DefaultMaximumPooledLength => 1024 * 1024;
    internal static int DefaultMaximumArraysPerBucket => 8;

    private readonly ArrayPool<byte> pool;
    private readonly int maximumPooledLength;

    public BoundedByteArrayPool()
        : this(
            ArrayPool<byte>.Create(DefaultMaximumPooledLength, DefaultMaximumArraysPerBucket),
            DefaultMaximumPooledLength)
    {
    }

    internal BoundedByteArrayPool(ArrayPool<byte> pool, int maximumPooledLength)
    {
        ArgumentNullException.ThrowIfNull(pool);
        if (maximumPooledLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maximumPooledLength));
        }

        this.pool = pool;
        this.maximumPooledLength = maximumPooledLength;
    }

    public ByteArrayRental Rent(int minimumLength)
    {
        if (minimumLength <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumLength));
        }

        return minimumLength <= maximumPooledLength
            ? new ByteArrayRental(pool.Rent(minimumLength), IsPooled: true)
            : new ByteArrayRental(GC.AllocateUninitializedArray<byte>(minimumLength), IsPooled: false);
    }

    public void Return(ByteArrayRental rental)
    {
        if (rental is { IsPooled: true, Buffer.Length: > 0 })
        {
            pool.Return(rental.Buffer);
        }
    }
}

internal readonly record struct ByteArrayRental(byte[] Buffer, bool IsPooled);
