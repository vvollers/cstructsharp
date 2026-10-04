namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Complete owned bitfield records, with unchanged fixture values and serialization as a control.</summary>
[BenchmarkCategory("Impact", "Bitfields")]
public class BitfieldBenchmarks
{
    private FixtureCase fixture = null!;
    private byte[] leaf = null!;
    private BitfieldLayout.Root value = null!;

    /// <summary>Checks every value, writer byte and independent result ownership before measuring.</summary>
    /// <exception cref="InvalidOperationException">Generated results differ from the fixture or share owned storage.</exception>
    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.LoadMatching("bitfield-x1k", typeof(BitfieldLayout));
        this.leaf = this.fixture.Bytes[..7];
        this.value = BitfieldLayout.Parse(this.fixture.Bytes);
        StructValue runtime = this.fixture.Layout.Parse(this.fixture.Bytes, "root");
        StructValue[] expected = runtime.Get<StructValue[]>("items");
        if (this.value.Items.Length != expected.Length)
        {
            throw new InvalidOperationException("Bitfield record counts differ.");
        }

        for (int index = 0; index < expected.Length; index++)
        {
            RequireValues(expected[index], this.value.Items[index]);
        }

        RequireValues(expected[0], BitfieldLayout.ParseRec(this.leaf));
        if (!this.fixture.Layout.Serialize("root", runtime).AsSpan().SequenceEqual(BitfieldLayout.Serialize(this.value)))
        {
            throw new InvalidOperationException("Bitfield writers disagree on complete output bytes.");
        }

        BitfieldLayout.Root independent = BitfieldLayout.Parse(this.fixture.Bytes);
        independent.Items[0].A ^= 1;
        if (independent.Items[0].A == this.value.Items[0].A || ReferenceEquals(independent.Items[0], independent.Items[1]))
        {
            throw new InvalidOperationException("Every bitfield result must own independent records.");
        }
    }

    /// <summary>Parses the full fixture into 1,024 owned records containing every declared value.</summary>
    /// <returns>The complete root and record array.</returns>
    [Benchmark]
    public BitfieldLayout.Root Generated_Bitfield1024_Parse() => BitfieldLayout.Parse(this.fixture.Bytes);

    /// <summary>Parses one seven-byte record to expose the per-leaf guard and materialization costs.</summary>
    /// <returns>The complete owned record.</returns>
    [Benchmark]
    public BitfieldLayout.Rec Generated_BitfieldLeaf_Parse() => BitfieldLayout.ParseRec(this.leaf);

    /// <summary>Serializes every record as a control for changes in the opposite direction.</summary>
    /// <returns>The complete owned encoded output, with anonymous padding written by the existing rules.</returns>
    [Benchmark]
    public byte[] Generated_Bitfield1024_Serialize() => BitfieldLayout.Serialize(this.value);

    /// <summary>Checks every field of one owned generated record against the runtime outside timing.</summary>
    /// <param name="expected">The complete runtime record.</param>
    /// <param name="actual">The corresponding generated record.</param>
    /// <exception cref="InvalidOperationException">At least one decoded value differs.</exception>
    private static void RequireValues(StructValue expected, BitfieldLayout.Rec actual)
    {
        if (actual.A != expected.Get<byte>("a") || actual.B != expected.Get<byte>("b") ||
            actual.C != expected.Get<ushort>("c") || actual.D != expected.Get<ushort>("d") ||
            actual.E != expected.Get<uint>("e") || actual.F != expected.Get<uint>("f"))
        {
            throw new InvalidOperationException("A generated bitfield value differs from the runtime.");
        }
    }
}
