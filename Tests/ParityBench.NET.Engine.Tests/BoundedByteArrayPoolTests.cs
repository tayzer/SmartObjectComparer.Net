using System.Buffers;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ParityBench.NET.Engine.Comparers;

namespace ParityBench.NET.Engine.Tests;

[TestClass]
public sealed class BoundedByteArrayPoolTests
{
    [TestMethod]
    public void Rent_WhenRequestExceedsLimit_DoesNotRetainArray()
    {
        TrackingArrayPool inner = new();
        BoundedByteArrayPool pool = new(inner, maximumPooledLength: 1024);

        ByteArrayRental rental = pool.Rent(1025);
        pool.Return(rental);

        Assert.IsFalse(rental.IsPooled);
        Assert.AreEqual(0, inner.RentCount);
        Assert.AreEqual(0, inner.ReturnCount);
    }

    [TestMethod]
    public void Rent_WhenRequestIsWithinLimit_ReturnsArrayExactlyOnce()
    {
        TrackingArrayPool inner = new();
        BoundedByteArrayPool pool = new(inner, maximumPooledLength: 1024);

        ByteArrayRental rental = pool.Rent(1024);
        pool.Return(rental);

        Assert.IsTrue(rental.IsPooled);
        Assert.AreEqual(1, inner.RentCount);
        Assert.AreEqual(1, inner.ReturnCount);
        Assert.AreSame(rental.Buffer, inner.LastReturned);
    }

    [TestMethod]
    public void DefaultPolicy_CapsArrayLengthAndPerBucketRetention()
    {
        Assert.AreEqual(1024 * 1024, BoundedByteArrayPool.DefaultMaximumPooledLength);
        Assert.AreEqual(8, BoundedByteArrayPool.DefaultMaximumArraysPerBucket);
    }

    private sealed class TrackingArrayPool : ArrayPool<byte>
    {
        public int RentCount { get; private set; }
        public int ReturnCount { get; private set; }
        public byte[]? LastReturned { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            RentCount++;
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            ReturnCount++;
            LastReturned = array;
        }
    }
}
