namespace CStructSharp.Tests;

using System.Collections.Concurrent;
using CStructSharp.Values;

/// <summary>
///     The representative layouts the differential sweeps (<see cref="EngineSweepTests"/>) run over: one per shape the
///     general reader and writer handle differently - a fixed record, a count-sized array, a conditional, nested
///     structs, an inline named struct that a qualified reference (<c>hdr.n</c>) names, anonymous promoted members
///     (which add no nesting level), a union, bitfields, an enum bitfield that sizes a later array, pointers (followed
///     after the struct, one with an <c>@count</c> target), terminated strings and arrays, a to-end array, a custom
///     codec, LEB128, fixed text, and caller variables in 128-bit expressions. Each is compiled packed and aligned
///     (<see cref="Variant"/>).
/// </summary>
internal static class EngineSweepLayouts
{
    /// <summary>Every sweep layout, by name, in a stable order.</summary>
    public static readonly SweepLayout[] All =
    [
        new(
            "fixed",
            "enum kind : uint8 { small = 1, large = 2 }; struct rec { uint16 id; int32 value; kind which; char tag[4]; float32 ratio; };",
            [0x07, 0x00, 0xFE, 0xFF, 0xFF, 0xFF, 0x02, (byte)'a', (byte)'b', 0x00, 0x00, 0x00, 0x00, 0xC0, 0x3F],
            ["rec.value", "rec.tag", "rec.ratio"],
            "rec.tag",
            [("rec.value", 5), ("rec.tag", "xyz")]),
        new(
            "count",
            "struct rec { uint8 n; uint16 items[n]; uint8 tail; };",
            [0x03, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00, 0x09],
            ["rec.items[2]", "rec.items", "rec.tail"],
            "rec.items",
            [("rec.items[1]", (ushort)7), ("rec.tail", (byte)1)],
            Names: ["n"]),
        new(
            "conditional",
            "struct rec { uint8 flag; if (flag == 1) { uint16 yes; } else { uint8 no; } uint8 tail; };",
            [0x01, 0x34, 0x12, 0x09],
            ["rec.yes", "rec.no", "rec.tail"],
            null,
            [("rec.yes", (ushort)5), ("rec.tail", (byte)2)],
            Names: ["flag"]),
        new(
            "nested",
            "struct inner { uint8 a; uint16 b; }; struct rec { uint8 n; inner items[n]; inner last; };",
            [0x02, 0x01, 0x02, 0x00, 0x03, 0x04, 0x00, 0x05, 0x06, 0x00],
            ["rec.items[1].b", "rec.last", "rec.last.b"],
            "rec.items",
            [("rec.items[1].b", (ushort)9), ("rec.last.a", (byte)8)],
            Names: ["n"]),
        new(
            "inline-qualified",
            "struct rec { struct { uint8 n; uint16 k; } hdr; uint16 items[hdr.n]; uint8 tail; };",
            [0x02, 0x05, 0x00, 0x01, 0x00, 0x02, 0x00, 0x09],
            ["rec.items[1]", "rec.hdr.k", "rec.tail"],
            "rec.items",
            [("rec.items[1]", (ushort)7), ("rec.hdr.k", (ushort)3)]),
        new(
            "promoted",
            "struct inner { uint8 a; uint16 b; }; struct rec { uint8 tag; struct { inner item; uint8 c[2]; }; uint8 tail; };",
            [0x07, 0x01, 0x02, 0x00, 0x03, 0x04, 0x09],
            ["rec.item.b", "rec.c", "rec.item"],
            "rec.c",
            [("rec.item.b", (ushort)9), ("rec.c[1]", (byte)5)]),
        new(
            "promoted-union",
            "struct inner { uint8 a; uint16 b; }; struct rec { uint8 tag; union { inner item; uint16 half; }; uint8 tail; };",
            [0x07, 0x01, 0x02, 0x00, 0x09],
            ["rec.item.b", "rec.half", "rec.item"],
            null,
            [("rec.item.b", (ushort)9), ("rec.tail", (byte)3)]),
        new(
            "union",
            "union u { uint8 a; uint16 b; uint32 c; }; struct rec { uint8 tag; u value; uint8 tail; };",
            [0x07, 0x44, 0x33, 0x22, 0x11, 0x09],
            ["rec.value", "rec.value.b", "rec.tail"],
            null,
            [("rec.value.b", (ushort)0xBEEF), ("rec.tail", (byte)3)]),
        new(
            "bitfields",
            "struct rec { uint8 low : 4; uint8 high : 4; uint16 mid : 9; uint16 top : 7; uint8 tail; };",
            [0x21, 0xFF, 0xFF, 0x09],
            ["rec.high", "rec.top", "rec.tail"],
            null,
            [("rec.high", 7), ("rec.mid", 3)]),
        new(
            "enum-bitfield",
            "enum kind : uint8 { A = 1, B = 2 }; struct rec { uint8 lo : 4; kind k : 4; uint16 items[k]; uint8 tail; };",
            [0x21, 0x01, 0x00, 0x02, 0x00, 0x09],
            ["rec.items[1]", "rec.k", "rec.tail"],
            "rec.items",
            [("rec.items[1]", (ushort)7), ("rec.tail", (byte)3)]),
        new(
            "pointers",
            "struct node { uint8 v; uint8 w; }; struct rec { uint8 tag; node *p; uint8 *q @count(n); uint8 n; uint8 tail; };",
            [0xAA, 0x06, 0x08, 0x03, 0x09, 0xEE, 0x11, 0x22, 0xA1, 0xA2, 0xA3],
            ["rec.p.value", "rec.q.value", "rec.p.address", "rec.tail"],
            null,
            [("rec.p.value.w", (byte)5), ("rec.tail", (byte)4)],
            PointerSize: 1,
            Names: ["n"]),
        new(
            "terminated",
            "struct entry { uint8 a; uint8 b; }; struct rec { char name[]; entry entries[]; uint8 tail; };",
            [(byte)'h', (byte)'i', 0x00, 0x01, 0x02, 0x03, 0x04, 0x00, 0x00, 0x09],
            ["rec.entries[1].b", "rec.name", "rec.tail"],
            "rec.entries",
            [("rec.name", "ho"), ("rec.entries[0].a", (byte)6)]),
        new(
            "to-end",
            "struct rec { uint16 magic; uint16 values[EOF]; };",
            [0x34, 0x12, 0x01, 0x00, 0x02, 0x00, 0x03, 0x00],
            ["rec.values[2]", "rec.values"],
            "rec.values",
            [("rec.values[1]", (ushort)9)]),
        new(
            "custom-codec",
            "struct rec { vlq count; vlq values[count]; uint8 tail; };",
            [0x02, 0x80, 0x01, 0x05, 0x09],
            ["rec.values[1]", "rec.tail"],
            "rec.values",
            [("rec.values[1]", 6u), ("rec.values[0]", 5u)],
            Names: ["count"],
            Codec: true),
        new(
            "leb128",
            "struct rec { uleb128_32 len; uint8 data[len]; uleb128_64 big; uint8 tail; };",
            [0x03, 0xA1, 0xA2, 0xA3, 0xE5, 0x8E, 0x26, 0x09],
            ["rec.big", "rec.data[2]", "rec.tail"],
            "rec.data",
            [("rec.big", 624486UL), ("rec.big", 1UL)],
            Names: ["len"]),
        new(
            "text",
            "struct rec { char name[6]; wchar< label[3]; utf8 word[4]; uint8 tail; };",
            [(byte)'a', (byte)'b', 0x00, 0x00, 0x00, 0x00, (byte)'x', 0x00, 0x00, 0x00, 0x00, 0x00, 0xC3, 0xA9, 0x00, 0x00, 0x09],
            ["rec.label", "rec.word", "rec.tail"],
            "rec.name",
            [("rec.name", "abc"), ("rec.word", "e")]),
        new(
            "variables",
            "struct rec { uint8 n; uint8 items[(extra * extra) / extra - extra + n + more]; uint8 tail; };",
            [0x02, 0x0A, 0x0B, 0x0C, 0x09],
            ["rec.items[2]", "rec.tail"],
            "rec.items",
            [("rec.items[0]", (byte)1)],
            Names: ["n", "extra", "more"],
            Variables: new Dictionary<string, int> { ["extra"] = 3, ["more"] = 1, }),
    ];

    /// <summary>The compiled variants, built once per layout and alignment.</summary>
    private static readonly ConcurrentDictionary<(string Name, bool Aligned), Variant> Variants = new();

    /// <summary>Gets the layout names as <see cref="DynamicDataAttribute"/> rows.</summary>
    public static IEnumerable<object[]> Names => All.Select(layout => new object[] { layout.Name, });

    /// <summary>Returns the packed or aligned variant of the named layout.</summary>
    /// <param name="name">A layout name from <see cref="All"/>.</param>
    /// <param name="aligned">Whether the variant uses the portable alignment rules.</param>
    /// <returns>The variant.</returns>
    public static Variant Get(string name, bool aligned)
        => Variants.GetOrAdd((name, aligned), key => new Variant(All.Single(layout => layout.Name == key.Name), key.Aligned));

    /// <summary>Returns both variants of the named layout: packed, then aligned.</summary>
    /// <param name="name">A layout name from <see cref="All"/>.</param>
    /// <returns>The variants.</returns>
    public static Variant[] Both(string name) => [Get(name, false), Get(name, true)];

    /// <summary>
    ///     One sweep layout: its definition, its packed input bytes, the paths the sweeps select, and the updates
    ///     they apply.
    /// </summary>
    /// <param name="Name">The layout's name in test data rows.</param>
    /// <param name="Definition">The layout source; its root struct is <c>rec</c>.</param>
    /// <param name="Data">A complete, valid packed encoding of <c>rec</c>.</param>
    /// <param name="Paths">Paths for selected reads and address resolution; the last is resolved in every sweep.</param>
    /// <param name="ArrayPath">A path whose length <c>GetArrayLength</c> reports, or <see langword="null"/>.</param>
    /// <param name="Updates">The paths and replacement values the update sweeps apply.</param>
    /// <param name="PointerSize">The pointer width in bytes.</param>
    /// <param name="Names">The identifiers the layout's expressions use, which the caller-variable sweep overrides.</param>
    /// <param name="Codec">Whether the layout registers <see cref="VlqCodec"/>.</param>
    /// <param name="Variables">The caller variables the layout needs, or <see langword="null"/> for none.</param>
    internal sealed record SweepLayout(
        string Name,
        string Definition,
        byte[] Data,
        string[] Paths,
        string? ArrayPath,
        (string Path, object Value)[] Updates,
        byte PointerSize = 8,
        string[]? Names = null,
        bool Codec = false,
        IReadOnlyDictionary<string, int>? Variables = null)
    {
        /// <summary>Gets whether the layout declares a pointer, so reads over a stream at a non-zero start need a relative origin.</summary>
        public bool HasPointers => this.Definition.Contains('*', StringComparison.Ordinal);
    }

    /// <summary>
    ///     A sweep layout compiled packed or aligned, with its input bytes for that placement and the value the
    ///     interpreter reads from them, which the write sweeps encode.
    /// </summary>
    internal sealed class Variant
    {
        /// <summary>
        ///     Compiles the layout. The aligned variant's input is the packed value written through the aligned layout,
        ///     so both variants hold the same values in their own placement.
        /// </summary>
        /// <param name="layout">The sweep layout.</param>
        /// <param name="aligned">Whether to apply the portable alignment rules.</param>
        public Variant(SweepLayout layout, bool aligned)
        {
            this.Source = layout;
            this.Aligned = aligned;
            this.Layout = Compile(layout, aligned);
            if (aligned)
            {
                CStruct packed = Compile(layout, false);
                StructValue value = packed.Parse(layout.Data.AsSpan(), "rec", layout.Variables, EngineSelections.InterpreterOnly(this.BaseRead()));

                // A pointer's target is not part of the written value, so the pointer layout (all one-byte members,
                // placed identically either way) keeps its packed bytes.
                this.Data = layout.HasPointers ? layout.Data : this.Layout.Serialize("rec", value, layout.Variables, EngineSelections.InterpreterOnly(new WriteOptions()));
            }
            else
            {
                this.Data = layout.Data;
            }

            this.Value = this.Layout.Parse(this.Data.AsSpan(), "rec", layout.Variables, EngineSelections.InterpreterOnly(this.BaseRead()));
        }

        /// <summary>Gets the sweep layout.</summary>
        public SweepLayout Source { get; }

        /// <summary>Gets whether this variant is aligned.</summary>
        public bool Aligned { get; }

        /// <summary>Gets the compiled layout.</summary>
        public CStruct Layout { get; }

        /// <summary>Gets the complete input for this placement.</summary>
        public byte[] Data { get; }

        /// <summary>Gets the value the interpreter reads from <see cref="Data"/>.</summary>
        public StructValue Value { get; }

        /// <summary>Gets the variant's name for failure messages, such as <c>count/aligned</c>.</summary>
        public string Name => this.Source.Name + (this.Aligned ? "/aligned" : "/packed");

        /// <summary>
        ///     The read options every sweep starts from for <paramref name="input"/>: the defaults, or for a pointer
        ///     layout relative addressing from the position the data starts at, so a stream that starts past its
        ///     beginning follows the same targets as a span.
        /// </summary>
        /// <param name="input">The input form, or <see langword="null"/> for a memory form.</param>
        /// <returns>The options.</returns>
        public ReadOptions BaseRead(EngineInput? input = null)
        {
            if (!this.Source.HasPointers)
            {
                return new ReadOptions();
            }

            long start = input is { } form && EngineStreams.IsStream(form) ? EngineStreams.StartOf(form) : 0;
            return new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = start, };
        }

        /// <summary>Compiles a sweep layout.</summary>
        /// <param name="layout">The sweep layout.</param>
        /// <param name="aligned">Whether to apply the portable alignment rules.</param>
        /// <returns>The compiled layout.</returns>
        private static CStruct Compile(SweepLayout layout, bool aligned)
            => new(
                layout.Definition,
                layout.PointerSize,
                aligned,
                compilationOptions: layout.Codec ? new CStructCompilationOptions { Codecs = [VlqCodec.Instance,], } : null);
    }
}
