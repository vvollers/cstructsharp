namespace CStructSharp.Benchmarks;

using System.Buffers;
using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Writes 256 complete multilingual records from unpadded strings to reusable memory output forms.</summary>
[BenchmarkCategory("Impact", "TextOutput")]
public class TextOutputBenchmarks
{
    private CStruct layout = null!;
    private StructValue value = null!;
    private byte[] destination = null!;
    private ArrayBufferWriter<byte> writer = null!;
    private MemoryStream stream = null!;

    /// <summary>Reuses the text-record fixture and verifies all output bytes for each measured destination.</summary>
    [GlobalSetup]
    public void Setup()
    {
        byte[] expected = TextRecordBenchmarks.CreateInput(256);
        this.layout = FixtureCase.CompileLike(typeof(TextRecordLayout));
        this.value = this.layout.Parse(expected, "root", options: new ReadOptions { TrimFixedText = true, });
        this.destination = new byte[expected.Length];
        this.writer = new ArrayBufferWriter<byte>(expected.Length);
        this.stream = new MemoryStream(expected.Length);
        Require(expected, this.layout.Serialize("root", this.value));
        if (this.Span() != expected.Length || this.BufferWriter() != expected.Length || this.Stream() != expected.Length)
        {
            throw new InvalidOperationException("Incomplete output.");
        }

        Require(expected, this.destination);
        Require(expected, this.writer.WrittenSpan);
        Require(expected, this.stream.ToArray());
        this.Async().GetAwaiter().GetResult();
        Require(expected, this.stream.ToArray());
    }

    /// <summary>Releases the reusable memory stream.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.stream?.Dispose();

    /// <summary>Writes complete records into existing caller-owned storage.</summary>
    /// <returns>The number of encoded bytes; setup verifies the whole destination.</returns>
    [Benchmark]
    public int Span() => this.layout.Serialize(this.destination.AsSpan(), "root", this.value);

    /// <summary>Appends complete records to a reusable buffer writer after clearing its preceding output.</summary>
    /// <returns>The number of committed bytes.</returns>
    [Benchmark]
    public long BufferWriter()
    {
        this.writer.Clear();
        return this.layout.Serialize(this.writer, "root", this.value);
    }

    /// <summary>Writes every field through the physical stream adapter into a reusable memory sink.</summary>
    /// <returns>The final stream position.</returns>
    [Benchmark]
    public long Stream()
    {
        this.stream.Position = 0;
        this.layout.Write(this.stream, "root", this.value);
        return this.stream.Position;
    }

    /// <summary>Measures owned serialization followed by the async write adapter into memory, without disk latency.</summary>
    /// <returns>The operation that writes all bytes; setup awaits it and verifies the complete sink.</returns>
    [Benchmark]
    public ValueTask Async()
    {
        this.stream.Position = 0;
        return this.layout.WriteAsync(this.stream, "root", this.value);
    }

    /// <summary>Rejects any missing or changed output byte outside timing.</summary>
    /// <param name="expected">The complete original fixture.</param>
    /// <param name="actual">One destination's complete bytes.</param>
    /// <exception cref="InvalidOperationException">The bytes differ.</exception>
    private static void Require(ReadOnlySpan<byte> expected, ReadOnlySpan<byte> actual)
    {
        if (!expected.SequenceEqual(actual))
        {
            throw new InvalidOperationException("Text output did not preserve every byte.");
        }
    }
}
