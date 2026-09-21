namespace CStructSharpTests;

using System.Buffers;
using CStructSharp;
using CStructSharp.Generated;
using CStructSharp.Streams;

/// <summary>
///     Pins the one buffering rule the async forms and the generated <c>Parse(Stream)</c> share: how many bytes are
///     read (the budget plus one, or a seekable stream's remaining length plus one), from where, what a non-seekable
///     stream loses, that cancellation before the first read touches nothing, and that a failing read returns the
///     pooled array.
/// </summary>
[TestClass]
public class AsyncStreamBufferTests
{
    /// <summary>A seekable stream is buffered from its position up to its remaining length plus one; the synchronous and asynchronous forms read the same bytes and leave the same position.</summary>
    [TestMethod]
    public async Task SeekableStream_BuffersTheRemainderPlusOne()
    {
        byte[] bytes = [0xEE, 1, 2, 3, 4, 5, 6,];
        using var sync = new MemoryStream(bytes);
        sync.Position = 1;
        byte[] buffer = ReadCursor.BufferStream(sync, null, out int length);
        try
        {
            Assert.AreEqual(6, length, "the remaining bytes; the plus-one byte is not there to read");
            CollectionAssert.AreEqual(bytes[1..], buffer[..length]);
            Assert.AreEqual(7L, sync.Position, "the loop leaves the position where it stopped; the operation sets the final one");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        string path = Path.Combine(Path.GetTempPath(), $"cstructsharp-async-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            file.Position = 1;
            (byte[] asyncBuffer, int asyncLength) = await ReadCursor.BufferStreamAsync(file, null, CancellationToken.None);
            try
            {
                Assert.AreEqual(6, asyncLength);
                CollectionAssert.AreEqual(bytes[1..], asyncBuffer[..asyncLength]);
                Assert.AreEqual(7L, file.Position);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(asyncBuffer);
            }
        }
        finally
        {
            File.Delete(path);
        }

        Assert.AreEqual(4, AsyncStreamBuffer.Capacity(new MemoryStream(new byte[10]) { Position = 7, }, null), "remaining 3 + 1");
        Assert.AreEqual(6, AsyncStreamBuffer.Capacity(new MemoryStream(new byte[10]), new ReadOptions { MaxTotalBytesRead = 5, }), "budget 5 + 1 when the stream has more");
    }

    /// <summary>A non-seekable stream is read up to the budget plus one byte, whatever the value needs, and those bytes are consumed.</summary>
    [TestMethod]
    public async Task NonSeekableStream_IsConsumedUpToTheBudgetPlusOne()
    {
        byte[] bytes = new byte[100];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)index;
        }

        var options = new ReadOptions { MaxTotalBytesRead = 40, };
        using var stream = new NonSeekableStream(bytes);
        (byte[] buffer, int length) = await AsyncStreamBuffer.RentAsync(stream, options, CancellationToken.None);
        try
        {
            Assert.AreEqual(41, length);
            Assert.AreEqual((byte)40, buffer[40]);
            Assert.AreEqual(41, stream.BytesRead, "the stream has given up the budget plus one");
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        using var rest = new NonSeekableStream(bytes[..10]);
        (buffer, length) = await AsyncStreamBuffer.RentAsync(rest, options, CancellationToken.None);
        ArrayPool<byte>.Shared.Return(buffer);
        Assert.AreEqual(10, length, "a stream that ends before the budget gives what it has");
    }

    /// <summary>A token cancelled before the first read throws before any byte is read; a failing read returns the pooled array.</summary>
    [TestMethod]
    public async Task Cancellation_AndFailures_LeaveNothingBehind()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var stream = new NonSeekableStream(new byte[8]);
        await Assert.ThrowsAsync<OperationCanceledException>(async () => await AsyncStreamBuffer.RentAsync(stream, null, cancelled.Token));
        Assert.AreEqual(0, stream.BytesRead);

        var pool = new CountingPool();
        using var failing = new NonSeekableStream(new byte[8]) { FailAfter = 3, };
        await Assert.ThrowsAsync<IOException>(async () => await AsyncStreamBuffer.RentAsync(failing, null, pool, CancellationToken.None));
        Assert.AreEqual(1, pool.Rented);
        Assert.AreEqual(1, pool.Returned, "the array goes back to the pool when the read fails");
        Assert.Throws<IOException>(() => AsyncStreamBuffer.Rent(new NonSeekableStream(new byte[8]) { FailAfter = 3, }, null, pool, out _));
        Assert.AreEqual(2, pool.Returned);
        Assert.Throws<ArgumentNullException>(() => AsyncStreamBuffer.Rent(null!, null, out _));
    }

    /// <summary>The options' token and the call's token are linked when both can cancel; otherwise the one that can is used.</summary>
    [TestMethod]
    public void Link_JoinsTheTwoTokens()
    {
        using var first = new CancellationTokenSource();
        using var second = new CancellationTokenSource();
        Assert.AreEqual(CancellationToken.None, AsyncStreamBuffer.Link((ReadOptions?)null, CancellationToken.None, out CancellationTokenSource? none));
        Assert.IsNull(none);
        Assert.AreEqual(second.Token, AsyncStreamBuffer.Link((ReadOptions?)null, second.Token, out CancellationTokenSource? onlySecond));
        Assert.IsNull(onlySecond);
        Assert.AreEqual(first.Token, AsyncStreamBuffer.Link(new ReadOptions { CancellationToken = first.Token, }, CancellationToken.None, out CancellationTokenSource? onlyFirst));
        Assert.IsNull(onlyFirst);
        CancellationToken linked = AsyncStreamBuffer.Link(new WriteOptions { CancellationToken = first.Token, }, second.Token, out CancellationTokenSource? both);
        Assert.IsNotNull(both);
        Assert.IsFalse(linked.IsCancellationRequested);
        second.Cancel();
        Assert.IsTrue(linked.IsCancellationRequested);
        both.Dispose();
    }

    /// <summary>A forward-only stream over an array that can fail after a number of bytes.</summary>
    internal sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private int position;

        public int BytesRead => this.position;

        public int FailAfter { get; init; } = int.MaxValue;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            if (this.position >= this.FailAfter)
            {
                throw new IOException("the source failed");
            }

            // One byte at a time on purpose: the loop must keep reading until the capacity or the end.
            int chunk = Math.Min(Math.Min(buffer.Length, 3), Math.Min(bytes.Length - this.position, this.FailAfter - this.position));
            if (chunk <= 0 && this.position < bytes.Length)
            {
                throw new IOException("the source failed");
            }

            bytes.AsSpan(this.position, chunk).CopyTo(buffer);
            this.position += chunk;
            return chunk;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new ValueTask<int>(this.Read(buffer.Span));
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingPool : ArrayPool<byte>
    {
        public int Rented { get; private set; }

        public int Returned { get; private set; }

        public override byte[] Rent(int minimumLength)
        {
            this.Rented++;
            return new byte[minimumLength];
        }

        public override void Return(byte[] array, bool clearArray = false)
        {
            this.Returned++;
        }
    }
}
