namespace CStructSharp.Benchmarks;

using System.Text;
using BenchmarkDotNet.Attributes;
using CStructSharp.Values;

/// <summary>
///     Synthetic layouts for fitting a cost model of the general reader over memory: each shape differs from another
///     by one kind of member, so the difference of two medians is the cost of that member.
/// </summary>
/// <remarks>
///     <para>
///     Every shape except <c>fixed-1</c> ends in <c>uint8 n; uint8 tail[n];</c> with <c>n = 0</c>. The runtime-counted
///     array gives the root no fixed size, so neither the direct reader nor a static plan applies to it and each
///     member before it goes through the general per-member reader.
///     </para>
///     <para>
///     The fitted quantities are: fixed cost per call (<c>general-1</c>, one scalar and one empty array), cost per
///     scalar field (<c>scalars-32</c> minus <c>scalars-16</c>, over 16), cost per conditional group
///     (<c>conditional-8</c> minus <c>plain-8</c>, over 8, on top of the one member each group reads), and cost per
///     <c>char[8]</c> (<c>char8-8</c> minus <c>general-1</c>, over 8). <c>fixed-1</c> is the same one-byte parse
///     through the fixed-root direct reader, for contrast.
///     </para>
/// </remarks>
[BenchmarkCategory("CostModel")]
public class CostModelBenchmarks
{
    private CStruct layout = null!;
    private byte[] bytes = [];

    /// <summary>The layout shape; see the class remarks for what each one isolates.</summary>
    [Params("fixed-1", "general-1", "scalars-16", "scalars-32", "plain-8", "conditional-8", "char8-8")]
    public string Shape { get; set; } = null!;

    /// <summary>Compiles the shape's layout and builds its input bytes.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><see cref="Shape"/> names no known shape.</exception>
    [GlobalSetup]
    public void Setup()
    {
        const string Tail = "uint8 n; uint8 tail[n];";
        (string members, int size) = this.Shape switch
        {
            "fixed-1" => ("uint8 a;", 1),
            "general-1" => (Tail, 1),
            "scalars-16" => (Repeat(16, index => $"uint32 f{index};") + Tail, (16 * 4) + 1),
            "scalars-32" => (Repeat(32, index => $"uint32 f{index};") + Tail, (32 * 4) + 1),
            "plain-8" => ("uint8 k;" + Repeat(8, index => $"uint32 a{index};") + Tail, 1 + (8 * 4) + 1),
            "conditional-8" => ("uint8 k;" + Repeat(8, index => $"if (k == 1) {{ uint32 a{index}; }} else {{ uint32 b{index}; }}") + Tail, 1 + (8 * 4) + 1),
            "char8-8" => (Repeat(8, index => $"char s{index}[8];") + Tail, (8 * 8) + 1),
            _ => throw new ArgumentOutOfRangeException(nameof(this.Shape), this.Shape, "Unknown cost-model shape."),
        };
        this.layout = new CStruct($"struct r {{ {members} }};");

        // All zeros keeps n = 0; the selector k is 1 so each conditional group reads its first branch, and the text
        // members hold eight letters each so the strings are not empty.
        this.bytes = new byte[size];
        if (this.Shape is "plain-8" or "conditional-8")
        {
            this.bytes[0] = 1;
        }
        else if (this.Shape == "char8-8")
        {
            this.bytes.AsSpan(0, 8 * 8).Fill((byte)'a');
        }
    }

    /// <summary>Parses the shape from its in-memory bytes.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseSpan() => this.layout.Parse(this.bytes, "r");

    /// <summary>Concatenates <paramref name="count"/> member declarations.</summary>
    /// <param name="count">The number of members.</param>
    /// <param name="member">Builds the declaration of the member with the given zero-based index.</param>
    /// <returns>The declarations separated by spaces, with a trailing space.</returns>
    private static string Repeat(int count, Func<int, string> member)
    {
        var text = new StringBuilder();
        for (int index = 0; index < count; index++)
        {
            text.Append(member(index)).Append(' ');
        }

        return text.ToString();
    }
}
