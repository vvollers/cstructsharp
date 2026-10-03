namespace FastImpact;

using System.Runtime.CompilerServices;
using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks;
using CStructSharp.Comparison.Variable;
using CStructSharp.Values;

/// <summary>Same packet parse with removable, redundant result validation and scratch allocation.</summary>
public class Controls
{
    private readonly PacketBenchmarks packet = new();

    /// <summary>Gets or sets how often the already parsed tag is checked.</summary>
    [Params(0, 1, 2, 4, 8, 16)]
    public int Checks { get; set; }

    /// <summary>Gets or sets the scratch payload length; zero disables the allocation.</summary>
    [Params(0, 32)]
    public int Scratch { get; set; }

    /// <summary>Prepares the original packet fixture and verifies equivalent encoded output before timing.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.packet.Setup();
        byte[] expected = this.packet.SerializeMapped();
        byte[] actual = this.packet.SerializeStructValue();
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidOperationException("Packet verification failed.");
        }

        var accessors = new PacketAccessors(PacketLayout.Layout);
        ulong fingerprint = PacketSample.Of(PacketSample.CreateDto());
        if (PacketSample.Of(this.Parse(), accessors) != fingerprint || PacketSample.Of(this.ParseLight(), accessors) != fingerprint)
        {
            throw new InvalidOperationException("Control output differs from the original packet.");
        }
    }

    /// <summary>Releases the original packet fixture.</summary>
    [GlobalCleanup]
    public void Cleanup() => this.packet.Cleanup();

    /// <summary>Parses once, optionally validates its tag again, and returns the same result.</summary>
    /// <returns>The original parsed packet.</returns>
    [Benchmark]
    public StructValue Parse()
    {
        StructValue result = this.packet.ParseSpan();
        for (int i = 0; i < this.Checks; i++)
        {
            Validate(result);
        }

        if (this.Scratch > 0)
        {
            Sink<byte[]>.Consume(new byte[this.Scratch]);
        }

        return result;
    }

    /// <summary>Parses once with a cheaper redundant count check, providing smaller timing changes.</summary>
    /// <returns>The same parsed packet, with optional scratch allocation.</returns>
    [Benchmark]
    public StructValue ParseLight()
    {
        StructValue result = this.packet.ParseSpan();
        for (int i = 0; i < this.Checks; i++)
        {
            ValidateCount(result);
        }

        if (this.Scratch > 0)
        {
            Sink<byte[]>.Consume(new byte[this.Scratch]);
        }

        return result;
    }

    /// <summary>Checks a count guaranteed by fixture construction without changing any parsed value.</summary>
    /// <param name="value">The completed parse.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ValidateCount(StructValue value)
    {
        if (value.Count == 0)
        {
            throw new InvalidOperationException("Empty packet.");
        }
    }

    /// <summary>Checks a tag that fixture construction already guarantees, preserving the parse's output.</summary>
    /// <param name="value">The completed parse.</param>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Validate(StructValue value)
    {
        if (!((IDictionary<string, object?>)value).ContainsKey("kind"))
        {
            throw new InvalidOperationException("Missing tag.");
        }
    }
}
