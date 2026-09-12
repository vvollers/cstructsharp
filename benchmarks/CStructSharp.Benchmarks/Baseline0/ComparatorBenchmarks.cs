namespace CStructSharp.Benchmarks.Baseline0;

using System.Buffers.Binary;
using System.Dynamic;
using System.Runtime.InteropServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;

/// <summary>
///     Lower-bound comparators: hand-written span readers with no schema, no validation, and typed results. Each
///     category pairs the hand-written floor (Baseline = true) with the library's parse of the same bytes, so the
///     Ratio column reports how far the schema-driven path is from a direct reader. Not apples-to-apples by design.
/// </summary>
[BenchmarkCategory("Baseline0", "Comparator")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ComparatorBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase primX1K = null!;
    private FixtureCase nested = null!;
    private FixtureCase arrayU32Be = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.primX1K = FixtureCase.Load("prim-le-x1k");
        this.nested = FixtureCase.Load("nested-x256");
        this.arrayU32Be = FixtureCase.Load("array-u32-be-262144");
    }

    // ---- S-PRIM single record -------------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public PrimRecordStruct HandWritten_PrimRecord_Typed()
    {
        return ReadPrimRecord(this.primRecord.Bytes);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public ExpandoObject HandWritten_PrimRecord_Expando()
    {
        PrimRecordStruct record = ReadPrimRecord(this.primRecord.Bytes);
        dynamic expando = new ExpandoObject();
        expando.a = record.A;
        expando.b = record.B;
        expando.c = record.C;
        expando.d = record.D;
        expando.e = record.E;
        expando.f = record.F;
        expando.g = record.G;
        return expando;
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public object Library_PrimRecord_ParseSpan()
    {
        return this.primRecord.ParseSpan();
    }

    // ---- S-PRIM × 1k -----------------------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimX1K")]
    public PrimRecordStruct[] HandWritten_PrimX1K_Typed()
    {
        ReadOnlySpan<byte> bytes = this.primX1K.Bytes;
        var records = new PrimRecordStruct[1024];
        for (int i = 0; i < records.Length; i++)
        {
            records[i] = ReadPrimRecord(bytes.Slice(i * 28, 28));
        }

        return records;
    }

    [Benchmark]
    [BenchmarkCategory("PrimX1K")]
    public object Library_PrimX1K_ParseSpan()
    {
        return this.primX1K.ParseSpan();
    }

    // ---- S-NESTED × 256 --------------------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public NestedTopStruct[] HandWritten_Nested256_Typed()
    {
        ReadOnlySpan<byte> bytes = this.nested.Bytes;
        var items = new NestedTopStruct[256];
        int offset = 0;
        for (int i = 0; i < items.Length; i++)
        {
            items[i] = ReadTop(bytes, ref offset);
        }

        return items;
    }

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public object Library_Nested256_ParseSpan()
    {
        return this.nested.ParseSpan();
    }

    // ---- S-ARRAY-U32 big-endian × 262144 ---------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("ArrayU32Be")]
    public uint[] HandWritten_ArrayU32Be_ReverseEndianness()
    {
        ReadOnlySpan<uint> source = MemoryMarshal.Cast<byte, uint>(this.arrayU32Be.Bytes);
        var values = new uint[source.Length];
        if (BitConverter.IsLittleEndian)
        {
            BinaryPrimitives.ReverseEndianness(source, values);
        }
        else
        {
            source.CopyTo(values);
        }

        return values;
    }

    [Benchmark]
    [BenchmarkCategory("ArrayU32Be")]
    public uint[] HandWritten_ArrayU32Be_ScalarLoop()
    {
        ReadOnlySpan<byte> bytes = this.arrayU32Be.Bytes;
        var values = new uint[bytes.Length / 4];
        for (int i = 0; i < values.Length; i++)
        {
            values[i] = BinaryPrimitives.ReadUInt32BigEndian(bytes.Slice(i * 4, 4));
        }

        return values;
    }

    [Benchmark]
    [BenchmarkCategory("ArrayU32Be")]
    public object Library_ArrayU32Be_ParseSpan()
    {
        return this.arrayU32Be.ParseSpan();
    }

    private static PrimRecordStruct ReadPrimRecord(ReadOnlySpan<byte> bytes)
    {
        return new PrimRecordStruct
        {
            A = bytes[0],
            B = BinaryPrimitives.ReadInt16LittleEndian(bytes.Slice(1)),
            C = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(3)),
            D = BinaryPrimitives.ReadInt64LittleEndian(bytes.Slice(7)),
            E = BinaryPrimitives.ReadSingleLittleEndian(bytes.Slice(15)),
            F = BinaryPrimitives.ReadDoubleLittleEndian(bytes.Slice(19)),
            G = bytes[27] != 0,
        };
    }

    private static NestedLeafStruct ReadLeaf(ReadOnlySpan<byte> bytes, ref int offset)
    {
        var leaf = new NestedLeafStruct
        {
            Kind = bytes[offset],
            Value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 1)),
        };
        offset += 5;
        return leaf;
    }

    private static NestedMidStruct ReadMid(ReadOnlySpan<byte> bytes, ref int offset)
    {
        NestedLeafStruct first = ReadLeaf(bytes, ref offset);
        NestedLeafStruct second = ReadLeaf(bytes, ref offset);
        ushort tail = BinaryPrimitives.ReadUInt16LittleEndian(bytes.Slice(offset));
        offset += 2;
        return new NestedMidStruct { First = first, Second = second, Tail = tail };
    }

    private static NestedTopStruct ReadTop(ReadOnlySpan<byte> bytes, ref int offset)
    {
        NestedMidStruct left = ReadMid(bytes, ref offset);
        NestedMidStruct right = ReadMid(bytes, ref offset);
        byte mark = bytes[offset++];
        return new NestedTopStruct { Left = left, Right = right, Mark = mark };
    }

    public struct PrimRecordStruct
    {
        public byte A;
        public short B;
        public uint C;
        public long D;
        public float E;
        public double F;
        public bool G;
    }

    public struct NestedLeafStruct
    {
        public byte Kind;
        public uint Value;
    }

    public struct NestedMidStruct
    {
        public NestedLeafStruct First;
        public NestedLeafStruct Second;
        public ushort Tail;
    }

    public struct NestedTopStruct
    {
        public NestedMidStruct Left;
        public NestedMidStruct Right;
        public byte Mark;
    }
}
