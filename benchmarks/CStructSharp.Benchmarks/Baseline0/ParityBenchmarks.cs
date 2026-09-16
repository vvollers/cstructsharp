namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>
///     S-PARITY cases that have no fixture form: a caller-registered codec (<see cref="ICustomCodec"/>), a prelude
///     shared by several layouts, and the sibling layouts <see cref="CStruct.WithEndianness"/> compiles from one
///     source. The fixture-backed parity cases (aliases, inline unions, flags, data-sized arrays, a synthetic root)
///     run through the ordinary Parse/Write/Update benchmarks.
/// </summary>
[BenchmarkCategory("Baseline0", "Parity")]
public class ParityBenchmarks
{
    private const string Prelude = "typedef uint32 DWORD_T; typedef uint16 WORD_T; flag ACCESS : uint16 { READ, WRITE, EXEC }; #define HEADER_SIZE 12\n";
    private const string Body = "struct root { DWORD_T magic; WORD_T version; ACCESS access; uint8 payload[HEADER_SIZE]; };";

    private CStruct varintLayout = null!;
    private CStruct siblingSource = null!;
    private CStructCompilationOptions preludeOptions = null!;
    private byte[] varintBytes = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.varintLayout = new CStruct(
            "struct entry { varint id; uint8 kind; }; struct root { varint count; entry items[count]; };",
            compilationOptions: new CStructCompilationOptions { Codecs = [new Varint(),], });
        var bytes = new List<byte> { 0x80, 0x08, }; // count 1024
        for (int index = 0; index < 1024; index++)
        {
            // Two-byte varints keep every element the same width as a uint16 would be.
            bytes.Add((byte)((index & 0x7F) | 0x80));
            bytes.Add((byte)(((index >> 7) & 0x7F) + 1));
            bytes.Add((byte)(index & 0xFF));
        }

        this.varintBytes = [.. bytes,];
        this.preludeOptions = new CStructCompilationOptions { Prelude = Prelude, };
        this.siblingSource = new CStruct(Prelude + Body);
    }

    /// <summary>1024 varint-led elements through a caller-registered codec (never plan-eligible).</summary>
    [Benchmark]
    public object ParseCustomCodecVarint1024()
    {
        return this.varintLayout.Parse(this.varintBytes.AsSpan(), "root");
    }

    /// <summary>The body compiled with the shared prelude: the prelude is parsed with the body, not cached separately.</summary>
    [Benchmark]
    public CStruct CompileWithPrelude()
    {
        return new CStruct(Body, compilationOptions: this.preludeOptions);
    }

    /// <summary>The same text compiled inline, for the prelude's overhead (must be within noise of each other).</summary>
    [Benchmark(Baseline = true)]
    public CStruct CompileInline()
    {
        return new CStruct(Prelude + Body);
    }

    /// <summary>A byte-order sibling through the shared cache: after the first call this is a cache hit.</summary>
    [Benchmark]
    public CStruct CompileSibling()
    {
        return this.siblingSource.WithEndianness(isLittleEndian: false);
    }

    private sealed class Varint : ICustomCodec
    {
        public string Name => "varint";

        public int? FixedSize => null;

        public int Alignment => 1;

        public object Read(Stream stream)
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                int next = stream.ReadByte();
                if (next < 0)
                {
                    throw new EndOfStreamException();
                }

                value |= (ulong)(next & 0x7F) << shift;
                if ((next & 0x80) == 0)
                {
                    return value;
                }
            }

            throw new InvalidDataException("varint longer than 64 bits");
        }

        public void Write(Stream stream, object value)
        {
            ulong remaining = Convert.ToUInt64(value);
            do
            {
                byte next = (byte)(remaining & 0x7F);
                remaining >>= 7;
                stream.WriteByte(remaining == 0 ? next : (byte)(next | 0x80));
            }
            while (remaining != 0);
        }
    }
}
