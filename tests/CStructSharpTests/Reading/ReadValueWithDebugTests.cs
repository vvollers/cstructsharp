namespace CStructSharp.Tests;

using System.Collections;
using System.Globalization;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Defines <c>ReadValueWithDebug</c> for every kind of selection: the value <c>ReadValue</c> returns, with one debug
///     record per value read under the path a whole-root debug parse gives it, on every input form.
/// </summary>
[TestClass]
public class ReadValueWithDebugTests
{
    /// <summary>
    ///     One unaligned layout, one-byte pointers, with every kind of member a path can select: a scalar at offset 1, a
    ///     nested struct, a union, a struct array, a scalar array, two bitfields sharing one byte, a struct pointer, a
    ///     scalar pointer, a pointer to a pointer, an enum and a terminated string.
    /// </summary>
    private const string Layout = """
                                  enum color : uint8 { red = 1, green = 2 };
                                  struct pair { uint8 a; uint8 b; };
                                  union choice { uint8 small; uint16 large; };
                                  struct root {
                                      uint8 head;
                                      uint16 value;
                                      pair inner;
                                      choice u;
                                      pair sarr[2];
                                      uint8 arr[3];
                                      uint8 low:3;
                                      uint8 high:5;
                                      pair *ptr;
                                      uint8 *bytep;
                                      uint8 **deep;
                                      color tint;
                                      cstring name;
                                  };
                                  """;

    /// <summary>
    ///     The bytes of <see cref="Layout"/>: head 0 (<c>AA</c>), value 1-2 (<c>0x1234</c>), inner 3-4, u 5-6, sarr 7-10,
    ///     arr 11-13, the bitfield byte 14 (<c>0xAB</c>: low 3, high 21), ptr 15 (to 22), bytep 16 (to 24), deep 17 (to
    ///     16, the stored <c>bytep</c>), tint 18 (green), name 19-21 (<c>"hi"</c>), then the pointed-to pair 22-23 and
    ///     byte 24.
    /// </summary>
    private static readonly byte[] Bytes =
    [
        0xAA, 0x34, 0x12, 1, 2, 0x78, 0x56, 3, 4, 5, 6, 7, 8, 9, 0xAB, 22, 24, 16, 2, (byte)'h', (byte)'i', 0, 0x11, 0x22, 0x33,
    ];

    /// <summary>
    ///     Each selection returns what <c>ReadValue</c> returns and records each value it read, as
    ///     <c>path [start,end) type = value</c>, on every input form.
    /// </summary>
    /// <remarks>
    ///     A scalar is one record (<c>root.value [1,3)</c>), an array one per element, a bitfield one over its byte, a
    ///     struct element its members under the indexed path (<c>root.sarr[1].a</c>), a pointer its stored address and its
    ///     struct target's members, <c>.address</c> the stored address alone, a scalar <c>.value</c> its target byte under
    ///     the pointer's path, an enum its number, and a terminated string its bytes with the terminator.
    /// </remarks>
    /// <param name="path">The selected path.</param>
    /// <param name="expected">The expected records, separated by <c>|</c>.</param>
    [TestMethod]
    [DataRow("root.value", "root.value [1,3) uint16 = 4660")]
    [DataRow("root.inner", "root.inner.a [3,4) uint8 = 1|root.inner.b [4,5) uint8 = 2")]
    [DataRow("root.inner.a", "root.inner.a [3,4) uint8 = 1")]
    [DataRow("root.u", "root.u.small [5,6) uint8 = 120|root.u.large [5,7) uint16 = 22136|root.u [5,7) choice = choice")]
    [DataRow("root.u.large", "root.u.large [5,7) uint16 = 22136")]
    [DataRow("root.sarr", "root.sarr[0].a [7,8) uint8 = 3|root.sarr[0].b [8,9) uint8 = 4|root.sarr[1].a [9,10) uint8 = 5|root.sarr[1].b [10,11) uint8 = 6")]
    [DataRow("root.sarr[1]", "root.sarr[1].a [9,10) uint8 = 5|root.sarr[1].b [10,11) uint8 = 6")]
    [DataRow("root.sarr[1].a", "root.sarr[1].a [9,10) uint8 = 5")]
    [DataRow("root.arr", "root.arr [11,12) uint8 = 7|root.arr [12,13) uint8 = 8|root.arr [13,14) uint8 = 9")]
    [DataRow("root.arr[1]", "root.arr [12,13) uint8 = 8")]
    [DataRow("root.low", "root.low [14,15) uint8 = 3")]
    [DataRow("root.high", "root.high [14,15) uint8 = 21")]
    [DataRow("root.ptr", "root.ptr.a [22,23) uint8 = 17|root.ptr.b [23,24) uint8 = 34|root.ptr [15,16) pair = pointer")]
    [DataRow("root.ptr.address", "root.ptr [15,16) pair = 22")]
    [DataRow("root.ptr.value", "root.ptr.a [22,23) uint8 = 17|root.ptr.b [23,24) uint8 = 34")]
    [DataRow("root.ptr.value.b", "root.ptr.b [23,24) uint8 = 34")]
    [DataRow("root.bytep.value", "root.bytep [24,25) uint8 = 51")]
    [DataRow("root.deep.value", "root.deep [16,17) uint8 = pointer")]
    [DataRow("root.deep.value.value", "root.deep [24,25) uint8 = 51")]
    [DataRow("root.tint", "root.tint [18,19) color = 2")]
    [DataRow("root.name", "root.name [19,22) ascii_string_zero = hi")]
    public void ReadValueWithDebug_EverySelection_RecordsEachValueRead(string path, string expected)
    {
        var layout = new CStruct(Layout, pointerSize: 1, aligned: false);
        string value = Render(layout.ReadValue(Bytes, path));

        foreach ((string form, ReadResult result) in ReadOnEveryForm(layout, Bytes, path))
        {
            Assert.AreEqual(value, Render(result.Value), form);
            Assert.AreEqual(expected, Records(result.Debug), form);
        }
    }

    /// <summary>
    ///     The records of a nested selection are the whole-root debug parse's records of the same bytes: every record the
    ///     selection makes appears there with the same path and range.
    /// </summary>
    /// <param name="path">The selected path.</param>
    [TestMethod]
    [DataRow("root.value")]
    [DataRow("root.inner")]
    [DataRow("root.u")]
    [DataRow("root.sarr")]
    [DataRow("root.sarr[1]")]
    [DataRow("root.arr")]
    [DataRow("root.low")]
    [DataRow("root.ptr")]
    [DataRow("root.tint")]
    [DataRow("root.name")]
    public void ReadValueWithDebug_NestedSelection_MatchesWholeRootRecords(string path)
    {
        var layout = new CStruct(Layout, pointerSize: 1, aligned: false);
        HashSet<string> whole = [.. layout.ParseWithDebug(Bytes, "root").Debug.Select(record => $"{record.Path} [{record.Start},{record.End})"),];

        foreach (DebugData record in layout.ReadValueWithDebug(Bytes, path).Debug)
        {
            Assert.Contains($"{record.Path} [{record.Start},{record.End})", whole, path);
        }
    }

    /// <summary>A bare struct root reads through its debug program: the value and records of <c>ParseWithDebug</c>.</summary>
    [TestMethod]
    public void ReadValueWithDebug_StructRoot_MatchesParseWithDebug()
    {
        var layout = new CStruct(Layout, pointerSize: 1, aligned: false);
        ParseResult parsed = layout.ParseWithDebug(Bytes, "root");

        foreach ((string form, ReadResult result) in ReadOnEveryForm(layout, Bytes, "root"))
        {
            Assert.AreEqual(Render(parsed.Value), Render(result.Value), form);
            Assert.AreEqual(Records(parsed.Debug), Records(result.Debug), form);
        }
    }

    /// <summary>
    ///     A scalar typedef root (<c>typedef uint16 word;</c>) and an enum root are values <c>Parse</c> rejects; the debug
    ///     value read returns them with one record each.
    /// </summary>
    [TestMethod]
    public void ReadValueWithDebug_ScalarAndEnumRoots_RecordTheirValue()
    {
        var layout = new CStruct("typedef uint16 word; enum color : uint8 { red = 1 };", pointerSize: 1);

        ReadResult word = layout.ReadValueWithDebug(new byte[] { 0x34, 0x12, }, "word");
        Assert.AreEqual((ushort)0x1234, word.Value);
        Assert.AreEqual("word [0,2) uint16 = 4660", Records(word.Debug));

        ReadResult color = layout.ReadValueWithDebug(new byte[] { 1, }, "color");
        Assert.AreEqual("red", color.Value!.ToString());
        Assert.AreEqual("color [0,1) color = 1", Records(color.Debug));
    }

    /// <summary>
    ///     A row of a two-dimensional struct array keeps the index that selected it in its elements' paths, as the
    ///     whole-root parse names them (<c>root.grid[1][0].a</c>); a fully indexed element parses under the same names.
    /// </summary>
    [TestMethod]
    public void ReadValueWithDebug_RowOfStructGrid_KeepsTheRowIndex()
    {
        var layout = new CStruct("struct pair { uint8 a; uint8 b; }; struct root { pair grid[2][2]; };", pointerSize: 1);
        byte[] bytes = [0, 1, 2, 3, 4, 5, 6, 7,];

        ReadResult row = layout.ReadValueWithDebug(bytes, "root.grid[1]");
        Assert.AreEqual(
            "root.grid[1][0].a [4,5) uint8 = 4|root.grid[1][0].b [5,6) uint8 = 5|root.grid[1][1].a [6,7) uint8 = 6|root.grid[1][1].b [7,8) uint8 = 7",
            Records(row.Debug));

        ParseResult cell = layout.ParseWithDebug(bytes, "root.grid[1][1]");
        Assert.AreEqual("root.grid[1][1].a [6,7) uint8 = 6|root.grid[1][1].b [7,8) uint8 = 7", Records(cell.Debug));
    }

    /// <summary>The struct target of one element of a pointer array records its members under the element's path.</summary>
    [TestMethod]
    public void ReadValueWithDebug_PointerArrayElementTarget_UsesTheElementPath()
    {
        var layout = new CStruct("struct pair { uint8 a; uint8 b; }; struct root { pair *items[2]; };", pointerSize: 1);
        byte[] bytes = [2, 4, 0x10, 0x20, 0x30, 0x40,];

        ReadResult target = layout.ReadValueWithDebug(bytes, "root.items[1].value");

        Assert.AreEqual("root.items[1].a [4,5) uint8 = 48|root.items[1].b [5,6) uint8 = 64", Records(target.Debug));
        CollectionAssert.IsSubsetOf(
            target.Debug.Select(record => record.Path).ToList(),
            layout.ParseWithDebug(bytes, "root").Debug.Select(record => record.Path).ToList());
    }

    /// <summary>
    ///     The table in <c>docs/guides/debug-data-and-addresses.md</c> ("Ranges of one selection"): each documented path
    ///     gives the documented records for the documented input.
    /// </summary>
    /// <param name="path">The documented path.</param>
    /// <param name="expected">The documented records, separated by <c>|</c>.</param>
    [TestMethod]
    [DataRow("root.value", "root.value [1,3) uint16 = 4660")]
    [DataRow("root.items[1]", "root.items[1].a [5,6) uint8 = 3|root.items[1].b [6,7) uint8 = 4")]
    [DataRow("root.items", "root.items[0].a [3,4) uint8 = 1|root.items[0].b [4,5) uint8 = 2|root.items[1].a [5,6) uint8 = 3|root.items[1].b [6,7) uint8 = 4")]
    [DataRow("root.codes", "root.codes [7,8) uint8 = 7|root.codes [8,9) uint8 = 8")]
    [DataRow("root.codes[1]", "root.codes [8,9) uint8 = 8")]
    [DataRow("root.high", "root.high [9,10) uint8 = 21")]
    [DataRow("root.link.address", "root.link [10,11) pair = 11")]
    [DataRow("root.link.value", "root.link.a [11,12) uint8 = 17|root.link.b [12,13) uint8 = 34")]
    public void ReadValueWithDebug_GuideExample_GivesTheDocumentedRecords(string path, string expected)
    {
        const string layout = """
                              struct pair { uint8 a; uint8 b; };
                              struct root {
                                  uint8 head;
                                  uint16 value;
                                  pair items[2];
                                  uint8 codes[2];
                                  uint8 low:3;
                                  uint8 high:5;
                                  pair *link;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: false);
        byte[] bytes = [0x00, 0x34, 0x12, 0x01, 0x02, 0x03, 0x04, 0x07, 0x08, 0xAB, 0x0B, 0x11, 0x22,];

        Assert.AreEqual(expected, Records(cstruct.ReadValueWithDebug(bytes, path).Debug));
    }

    /// <summary>A null pointer's <c>.value</c> reads nothing: the value is null and there are no records.</summary>
    [TestMethod]
    public void ReadValueWithDebug_NullPointerValue_ReturnsNullWithoutRecords()
    {
        var layout = new CStruct("struct root { uint8 *p; };", pointerSize: 1);

        ReadResult result = layout.ReadValueWithDebug(new byte[] { 0, }, "root.p.value");

        Assert.IsNull(result.Value);
        Assert.IsEmpty(result.Debug);
    }

    /// <summary>
    ///     <c>Parse</c> and <c>ParseWithDebug</c> return one struct: a whole struct array, a row of one, and a scalar fail
    ///     with the error that points to the value reads, on every input form.
    /// </summary>
    /// <param name="path">The path that selects no single struct.</param>
    [TestMethod]
    [DataRow("root.sarr")]
    [DataRow("root.value")]
    [DataRow("root.ptr.address")]
    public void Parse_PathWithoutOneStruct_PointsToReadValue(string path)
    {
        var layout = new CStruct(Layout, pointerSize: 1, aligned: false);
        using var stream = new MemoryStream(Bytes);

        CStructPathException span = Assert.Throws<CStructPathException>(() => layout.Parse(Bytes, path));
        StringAssert.Contains(span.Message, "use ReadValue or ReadValueWithDebug");
        CStructPathException debug = Assert.Throws<CStructPathException>(() => layout.ParseWithDebug(stream, path));
        StringAssert.Contains(debug.Message, "use ReadValue or ReadValueWithDebug");
        CStructPathException sequence = Assert.Throws<CStructPathException>(() => layout.Parse(ChunkedSequence.Of(Bytes), path));
        StringAssert.Contains(sequence.Message, "use ReadValue or ReadValueWithDebug");
    }

    /// <summary>A debug value read needs a seekable stream, as every debug read does.</summary>
    [TestMethod]
    public void ReadValueWithDebug_NonSeekableStream_IsAnArgumentError()
    {
        var layout = new CStruct(Layout, pointerSize: 1, aligned: false);
        using var stream = new AsyncStreamBufferTests.NonSeekableStream(Bytes);

        Assert.Throws<ArgumentException>(() => layout.ReadValueWithDebug(stream, "root.value"));
    }

    /// <summary>
    ///     Reads a path with <c>ReadValueWithDebug</c> from every input form: a span, an array, memory, an exposed memory
    ///     stream, a stream without an exposed buffer, a multi-segment sequence, and the async form over a memory stream.
    /// </summary>
    /// <param name="layout">The layout.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="path">The path.</param>
    /// <returns>Each form's name and result.</returns>
    private static IEnumerable<(string Form, ReadResult Result)> ReadOnEveryForm(CStruct layout, byte[] bytes, string path)
    {
        yield return ("span", layout.ReadValueWithDebug(bytes.AsSpan(), path));
        yield return ("array", layout.ReadValueWithDebug(bytes, path));
        yield return ("memory", layout.ReadValueWithDebug(bytes.AsMemory(), path));
        using (var exposed = new MemoryStream(bytes))
        {
            yield return ("stream", layout.ReadValueWithDebug(exposed, path));
        }

        using (var hidden = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false))
        {
            yield return ("hidden stream", layout.ReadValueWithDebug(hidden, path));
        }

        yield return ("sequence", layout.ReadValueWithDebug(ChunkedSequence.Of(bytes), path));
        using (var awaited = new MemoryStream(bytes))
        {
            yield return ("async", layout.ReadValueWithDebugAsync(awaited, path).AsTask().GetAwaiter().GetResult());
        }
    }

    /// <summary>
    ///     Renders records as <c>path [start,end) type = value</c> joined by <c>|</c>; a union's value is its name and a
    ///     pointer's value is <c>pointer</c>, because their own renderings hold nested values.
    /// </summary>
    /// <param name="records">The records.</param>
    /// <returns>The rendering.</returns>
    private static string Records(IReadOnlyList<DebugData> records)
        => string.Join(
            "|",
            records.Select(
                record => string.Create(CultureInfo.InvariantCulture, $"{record.Path} [{record.Start},{record.End}) {record.TypeName} = ") + record.Value switch
                {
                    UnionValue union => union.UnionName,
                    Pointer => "pointer",
                    _ => Convert.ToString(record.Value, CultureInfo.InvariantCulture),
                }));

    /// <summary>Renders a value for comparison: a list or array element by element, anything else by its own text.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendering.</returns>
    private static string Render(object? value)
        => value switch
        {
            null => "null",
            string text => text,
            IEnumerable elements and not StructValue and not UnionValue => "[" + string.Join(", ", elements.Cast<object?>().Select(Render)) + "]",
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty,
        };
}
