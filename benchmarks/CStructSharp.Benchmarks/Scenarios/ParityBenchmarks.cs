namespace CStructSharp.Benchmarks.Scenarios;

using System.Buffers;
using System.Globalization;
using BenchmarkDotNet.Attributes;
using CStructSharp.Codecs;

/// <summary>
///     S-PARITY cases that have no fixture form: a caller-registered codec (<see cref="ICustomCodec"/>), a prelude
///     shared by several layouts, and the sibling layouts <see cref="CStruct.WithEndianness"/> compiles from one
///     source. The fixture-backed parity cases (aliases, inline unions, flags, data-sized arrays, a synthetic root)
///     run through the ordinary Parse/Write/Update benchmarks.
/// </summary>
[BenchmarkCategory("Scenario", "Parity")]
public class ParityBenchmarks
{
    private const string Prelude = "typedef uint32 DWORD_T; typedef uint16 WORD_T; flag ACCESS : uint16 { READ, WRITE, EXEC }; #define HEADER_SIZE 12\n";
    private const string Body = "struct root { DWORD_T magic; WORD_T version; ACCESS access; uint8 payload[HEADER_SIZE]; };";

    private CStruct varintLayout = null!;
    private CStruct siblingSource = null!;
    private CStructCompilationOptions preludeOptions = null!;
    private byte[] varintBytes = null!;

    /// <summary>Compiles the codec layout and the byte-order source layout, and prepares the prelude options and codec input.</summary>
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
    /// <returns>The parsed root value, returned so the work is not eliminated.</returns>
    [Benchmark]
    public object ParseCustomCodecVarint1024()
    {
        return this.varintLayout.Parse(this.varintBytes.AsSpan(), "root");
    }

    /// <summary>The body compiled with the shared prelude: the prelude is parsed with the body, not cached separately.</summary>
    /// <returns>The layout compiled from the body and the prelude option.</returns>
    [Benchmark]
    public CStruct CompileWithPrelude()
    {
        return new CStruct(Body, compilationOptions: this.preludeOptions);
    }

    /// <summary>The same text compiled inline, for the prelude's overhead (must be within noise of each other).</summary>
    /// <returns>The layout compiled from the prelude and body as one source text.</returns>
    [Benchmark(Baseline = true)]
    public CStruct CompileInline()
    {
        return new CStruct(Prelude + Body);
    }

    /// <summary>A byte-order sibling through the shared cache: after the first call this is a cache hit.</summary>
    /// <returns>The big-endian sibling of the source layout.</returns>
    [Benchmark]
    public CStruct CompileSibling()
    {
        return this.siblingSource.WithEndianness(isLittleEndian: false);
    }

    /// <summary>An unsigned LEB128 integer: seven bits per byte, low group first, the high bit set on every byte but the last.</summary>
    private sealed class Varint : ICustomCodec
    {
        public string Name => "varint";

        public int? FixedSize => null;

        public int Alignment => 1;

        /// <summary>Decodes one integer of at most ten bytes.</summary>
        /// <param name="source">The bytes at the member's offset.</param>
        /// <param name="value">The decoded <see cref="ulong"/>, or <see langword="null"/> when decoding fails.</param>
        /// <param name="bytesConsumed">The number of bytes the integer occupies.</param>
        /// <returns><see cref="OperationStatus.Done"/>, <see cref="OperationStatus.NeedMoreData"/> for a cut integer, or <see cref="OperationStatus.InvalidData"/> after ten bytes.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            ulong result = 0;
            for (int index = 0; index < source.Length && index < 10; index++)
            {
                result |= (ulong)(source[index] & 0x7F) << (7 * index);
                if ((source[index] & 0x80) == 0)
                {
                    value = result;
                    bytesConsumed = index + 1;
                    return OperationStatus.Done;
                }
            }

            value = null;
            bytesConsumed = 0;
            return source.Length >= 10 ? OperationStatus.InvalidData : OperationStatus.NeedMoreData;
        }

        /// <summary>Encodes one integer.</summary>
        /// <param name="destination">The bytes at the member's offset.</param>
        /// <param name="value">A value convertible to <see cref="ulong"/>.</param>
        /// <param name="bytesWritten">The number of bytes written.</param>
        /// <returns><see cref="OperationStatus.Done"/>, or <see cref="OperationStatus.DestinationTooSmall"/> when the encoding does not fit.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            ulong remaining = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
            bytesWritten = 0;
            do
            {
                if (bytesWritten == destination.Length)
                {
                    return OperationStatus.DestinationTooSmall;
                }

                byte next = (byte)(remaining & 0x7F);
                remaining >>= 7;
                destination[bytesWritten++] = remaining == 0 ? next : (byte)(next | 0x80);
            }
            while (remaining != 0);

            return OperationStatus.Done;
        }
    }
}
