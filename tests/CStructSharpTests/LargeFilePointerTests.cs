namespace CStructSharpTests;

using System.Buffers.Binary;
using CStructSharp;

/// <summary>Pointer distance must never be confused with bytes decoded or allocated.</summary>
[TestClass]
public class LargeFilePointerTests
{
    private const long Far = (8L << 40) + 65535;
    private const long Middle = (1L << 32) + 31;
    private const long Near = 4096;
    private const string Definition = "struct node { uint32 value; node *next; }; struct root { node *nodes[3]; uint32 tail; };";

    /// <summary>Several forward and backward targets in an eight-terabyte source work with default options.</summary>
    [TestMethod]
    public void ScatteredPointers_UseDefaultOptionsWithoutReadingTheGaps()
    {
        var layout = new CStruct(Definition, pointerSize: 8);
        using var source = CreateSource();
        dynamic parsed = layout.ParseStream(source, "root");
        Assert.AreEqual(111U, (uint)parsed.nodes[0].Value.value);
        Assert.AreEqual(222U, (uint)parsed.nodes[0].Value.next.Value.value);
        Assert.AreEqual(333U, (uint)parsed.nodes[0].Value.next.Value.next.Value.value);
        Assert.AreEqual(222U, (uint)parsed.nodes[1].Value.value);
        Assert.AreEqual(333U, (uint)parsed.nodes[2].Value.value);
        Assert.AreEqual(0xABCDEF01U, (uint)parsed.tail);
        Assert.AreEqual(28L, source.Position);
        Assert.IsLessThan(512L, source.BytesRead);
    }

    /// <summary>Debug parsing and selected paths retain exact 64-bit addresses under a tiny read budget.</summary>
    [TestMethod]
    public void DebugAndSelectedReads_HonorDecodedByteBudgetNotFileLength()
    {
        var layout = new CStruct(Definition, pointerSize: 8);
        var options = new ReadOptions { MaxTotalBytesRead = 512, };
        using var source = CreateSource();
        _ = layout.ParseStreamWithDebug(source, "root", options: options);
        Assert.IsLessThanOrEqualTo(512L, source.BytesRead);
        source.Position = 0;
        Assert.AreEqual(Far, layout.ResolveAddress(source, "root.nodes[0].value.value", options: options));
        Assert.AreEqual(0L, source.Position);
        Assert.AreEqual(Near, layout.ResolveAddress(source, "root.nodes[0].value.next.value.next.value.value", options: options));
        Assert.AreEqual(0L, source.Position);
        dynamic selected = layout.ParseStream(source, "root.nodes[1].value", options: options);
        Assert.AreEqual(222U, (uint)selected.value);
    }

    /// <summary>Reading actual data still observes an explicitly chosen small budget.</summary>
    [TestMethod]
    public void SmallBudget_StillRejectsTooMuchDecodedData()
    {
        var layout = new CStruct(Definition, pointerSize: 8);
        using var source = CreateSource();
        Assert.Throws<CStructReadLimitException>(() => layout.ParseStream(source, "root", options: new ReadOptions { MaxTotalBytesRead = 8, }));
    }

    private static SparseSource CreateSource()
    {
        byte[] header = new byte[28];
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(0), Far);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(8), Middle);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(16), Near);
        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan(24), 0xABCDEF01U);
        return new SparseSource(new Dictionary<long, byte[]> { [0] = header, [Far] = Node(111, Middle), [Middle] = Node(222, Near), [Near] = Node(333, 0), });
    }

    private static byte[] Node(uint value, long next)
    {
        byte[] bytes = new byte[12];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(4), next);
        return bytes;
    }

    private sealed class SparseSource(Dictionary<long, byte[]> pieces) : Stream
    {
        public long BytesRead { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => false;

        public override long Length => Far + 12;

        public override long Position { get; set; }

        public override int Read(byte[] buffer, int offset, int count) => this.Read(buffer.AsSpan(offset, count));

        public override int Read(Span<byte> buffer)
        {
            int count = (int)Math.Min(buffer.Length, Math.Max(0, this.Length - this.Position));
            buffer[..count].Clear();
            foreach ((long start, byte[] bytes) in pieces)
            {
                long from = Math.Max(this.Position, start);
                long to = Math.Min(this.Position + count, start + bytes.Length);
                if (to > from)
                {
                    bytes.AsSpan((int)(from - start), (int)(to - from)).CopyTo(buffer[(int)(from - this.Position)..]);
                }
            }

            this.Position += count;
            this.BytesRead += count;
            return count;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            long basis = origin switch
            {
                SeekOrigin.Begin => 0,
                SeekOrigin.Current => this.Position,
                SeekOrigin.End => this.Length,
                _ => throw new ArgumentOutOfRangeException(nameof(origin)),
            };
            this.Position = checked(offset + basis);
            return this.Position;
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
