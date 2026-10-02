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
    ///     An unaligned layout, one-byte pointers, of one pointer per kind of target: a struct, a union, an enum, a string, a
    ///     pointer to a pointer, a pointer array to structs and one to bytes.
    /// </summary>
    private const string PointerLayout = """
                                         enum color : uint8 { red = 1, green = 2 };
                                         struct pair { uint8 a; uint8 b; };
                                         union choice { uint8 small; uint16 large; };
                                         struct root {
                                             pair *ptr;
                                             choice *uptr;
                                             color *tintp;
                                             char *text;
                                             uint8 **deep;
                                             pair *items[2];
                                             uint8 *bytes[2];
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
    ///     The bytes of <see cref="PointerLayout"/>: the pointers 0-8 (ptr to 9, uptr to 11, tintp to 13, text to 14, deep
    ///     to 17, items to 9 and 11, bytes to 13 and 18), then the pair 9-10, the union 11-12, green at 13, <c>"hi"</c> at
    ///     14-16, the intermediate pointer at 17 (to 18) and the byte 18.
    /// </summary>
    private static readonly byte[] PointerBytes = [9, 11, 13, 14, 17, 9, 11, 13, 18, 0x11, 0x22, 0x34, 0x12, 2, (byte)'h', (byte)'i', 0, 18, 0x55,];

    /// <summary>
    ///     Each selection returns what <c>ReadValue</c> returns and records each value it read, as
    ///     <c>path [start,end) type = value</c>, on every input form.
    /// </summary>
    /// <remarks>
    ///     A scalar is one record (<c>root.value [1,3)</c>), an array one per element, a bitfield one over its byte, a
    ///     struct element its members under the indexed path (<c>root.sarr[1].a</c>), a pointer its stored address and its
    ///     struct target's members under <c>root.ptr.value</c>, <c>.address</c> the stored address alone under the
    ///     pointer's path, a scalar <c>.value</c> its target byte under the selected path (<c>root.bytep.value</c>), a
    ///     pointer left to follow its own target and then its stored address, an enum its number, and a terminated string
    ///     its bytes with the terminator.
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
    [DataRow("root.ptr", "root.ptr.value.a [22,23) uint8 = 17|root.ptr.value.b [23,24) uint8 = 34|root.ptr [15,16) pair = pointer")]
    [DataRow("root.ptr.address", "root.ptr [15,16) pair = 22")]
    [DataRow("root.ptr.value", "root.ptr.value.a [22,23) uint8 = 17|root.ptr.value.b [23,24) uint8 = 34")]
    [DataRow("root.ptr.value.b", "root.ptr.value.b [23,24) uint8 = 34")]
    [DataRow("root.bytep.value", "root.bytep.value [24,25) uint8 = 51")]
    [DataRow("root.deep.address", "root.deep [17,18) uint8 = 16")]
    [DataRow("root.deep.value", "root.deep.value.value [24,25) uint8 = 51|root.deep.value [16,17) uint8 = pointer")]
    [DataRow("root.deep.value.address", "root.deep.value [16,17) uint8 = 24")]
    [DataRow("root.deep.value.value", "root.deep.value.value [24,25) uint8 = 51")]
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
    [DataRow("root.ptr.address")]
    [DataRow("root.ptr.value")]
    [DataRow("root.ptr.value.b")]
    [DataRow("root.bytep.value")]
    [DataRow("root.deep.value")]
    [DataRow("root.deep.value.address")]
    [DataRow("root.deep.value.value")]
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

    /// <summary>
    ///     The struct target of one element of a pointer array records its members under the element's path extended by
    ///     <c>value</c>.
    /// </summary>
    [TestMethod]
    public void ReadValueWithDebug_PointerArrayElementTarget_UsesTheElementPath()
    {
        var layout = new CStruct("struct pair { uint8 a; uint8 b; }; struct root { pair *items[2]; };", pointerSize: 1);
        byte[] bytes = [2, 4, 0x10, 0x20, 0x30, 0x40,];

        ReadResult target = layout.ReadValueWithDebug(bytes, "root.items[1].value");

        Assert.AreEqual("root.items[1].value.a [4,5) uint8 = 48|root.items[1].value.b [5,6) uint8 = 64", Records(target.Debug));
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
    [DataRow("root.link", "root.link.value.a [11,12) uint8 = 17|root.link.value.b [12,13) uint8 = 34|root.link [10,11) pair = pointer")]
    [DataRow("root.link.address", "root.link [10,11) pair = 11")]
    [DataRow("root.link.value", "root.link.value.a [11,12) uint8 = 17|root.link.value.b [12,13) uint8 = 34")]
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

    /// <summary>
    ///     A whole-root debug parse records every followed target under the pointer's path extended by one <c>value</c> per
    ///     level: a struct's members, a union's views and own record, an enum's number, a string's bytes, an intermediate
    ///     pointer and its byte, and each element target of a pointer array; the pointers' own records keep their paths,
    ///     an element of a pointer array its index (<c>root.bytes[1]</c>) whatever its target.
    /// </summary>
    [TestMethod]
    public void ParseWithDebug_PointerTargets_RecordUnderValuePaths()
    {
        var layout = new CStruct(PointerLayout, pointerSize: 1, aligned: false);

        ParseResult parsed = layout.ParseWithDebug(PointerBytes, "root");

        Assert.AreEqual(
            string.Join(
                "|",
                "root.ptr [0,1) pair = pointer",
                "root.uptr [1,2) choice = pointer",
                "root.tintp [2,3) color = pointer",
                "root.text [3,4) char = pointer",
                "root.deep [4,5) uint8 = pointer",
                "root.items[0] [5,6) pair = pointer",
                "root.items[1] [6,7) pair = pointer",
                "root.bytes[0] [7,8) uint8 = pointer",
                "root.bytes[1] [8,9) uint8 = pointer",
                "root.ptr.value.a [9,10) uint8 = 17",
                "root.ptr.value.b [10,11) uint8 = 34",
                "root.uptr.value.small [11,12) uint8 = 52",
                "root.uptr.value.large [11,13) uint16 = 4660",
                "root.uptr.value [11,13) choice = choice",
                "root.tintp.value [13,14) color = 2",
                "root.text.value [14,17) char = hi",
                "root.deep.value.value [18,19) uint8 = 85",
                "root.deep.value [17,18) uint8 = pointer",
                "root.items[0].value.a [9,10) uint8 = 17",
                "root.items[0].value.b [10,11) uint8 = 34",
                "root.items[1].value.a [11,12) uint8 = 52",
                "root.items[1].value.b [12,13) uint8 = 18",
                "root.bytes[0].value [13,14) uint8 = 2",
                "root.bytes[1].value [18,19) uint8 = 85"),
            Records(parsed.Debug));
    }

    /// <summary>
    ///     No record of a pointer target shares a path with a pointer's own record: before every followed level added a
    ///     <c>value</c> segment, a union target's own record took the pointer's path (<c>root.uptr</c>) over different
    ///     bytes. In <see cref="PointerLayout"/>, which has no scalar array, no two records share a path at all: each
    ///     element of a pointer array keeps its index, because each has a target of its own.
    /// </summary>
    [TestMethod]
    public void ParseWithDebug_PointerTargets_NeverShareAPointersPath()
    {
        var layout = new CStruct(PointerLayout, pointerSize: 1, aligned: false);

        IReadOnlyList<DebugData> records = layout.ParseWithDebug(PointerBytes, "root").Debug;

        HashSet<string> pointers = [.. records.Where(record => record.Value is Pointer).Select(record => record.Path),];
        foreach (DebugData record in records.Where(record => record.Value is not Pointer))
        {
            Assert.DoesNotContain(record.Path, pointers, $"{record.Path} [{record.Start},{record.End})");
        }

        CollectionAssert.AllItemsAreUnique(records.Select(record => record.Path).ToList());
    }

    /// <summary>
    ///     A selection through any pointer of <see cref="PointerLayout"/> records what the whole-root debug parse records
    ///     for the same bytes: the same paths with the same ranges.
    /// </summary>
    /// <param name="path">The selected path.</param>
    [TestMethod]
    [DataRow("root.ptr.value")]
    [DataRow("root.uptr")]
    [DataRow("root.uptr.value")]
    [DataRow("root.uptr.value.large")]
    [DataRow("root.tintp.value")]
    [DataRow("root.text.value")]
    [DataRow("root.deep")]
    [DataRow("root.deep.value")]
    [DataRow("root.deep.value.value")]
    [DataRow("root.items[1].value")]
    [DataRow("root.items[1].value.b")]
    [DataRow("root.bytes[1].value")]
    public void ReadValueWithDebug_PointerSelection_MatchesWholeRootRecords(string path)
    {
        var layout = new CStruct(PointerLayout, pointerSize: 1, aligned: false);
        HashSet<string> whole = [.. layout.ParseWithDebug(PointerBytes, "root").Debug.Select(record => $"{record.Path} [{record.Start},{record.End})"),];

        IReadOnlyList<DebugData> records = layout.ReadValueWithDebug(PointerBytes, path).Debug;

        Assert.IsNotEmpty(records, path);
        foreach (DebugData record in records)
        {
            Assert.Contains($"{record.Path} [{record.Start},{record.End})", whole, path);
        }
    }

    /// <summary>
    ///     Every record of a whole-root parse of <see cref="PointerLayout"/> names a path that selects its value: a selection
    ///     of that path records the same path over the same bytes, and resolves to the record's first byte.
    /// </summary>
    [TestMethod]
    public void ParseWithDebug_PointerRecordPaths_AreSelectionPaths()
    {
        var layout = new CStruct(PointerLayout, pointerSize: 1, aligned: false);

        foreach (DebugData record in layout.ParseWithDebug(PointerBytes, "root").Debug)
        {
            string expected = $"{record.Path} [{record.Start},{record.End})";
            IReadOnlyList<DebugData> selected = layout.ReadValueWithDebug(PointerBytes, record.Path).Debug;
            Assert.Contains(expected, selected.Select(item => $"{item.Path} [{item.Start},{item.End})").ToList(), expected);
            Assert.AreEqual(record.Start, layout.ResolveAddress(PointerBytes, record.Path), expected);
        }
    }

    /// <summary>
    ///     The scalar example in <c>docs/guides/debug-data-and-addresses.md</c> ("Records of a pointer and its target"):
    ///     for <c>struct box { uint8 *flag; }</c> and input <c>01 2A</c>, the parse records the stored address under
    ///     <c>box.flag</c> and the byte 42 under <c>box.flag.value</c>.
    /// </summary>
    [TestMethod]
    public void ParseWithDebug_GuideScalarPointer_RecordsAddressAndTargetApart()
    {
        var layout = new CStruct("struct box { uint8 *flag; };", pointerSize: 1);

        ParseResult parsed = layout.ParseWithDebug(new byte[] { 0x01, 0x2A, }, "box");

        Assert.AreEqual("box.flag [0,1) uint8 = pointer|box.flag.value [1,2) uint8 = 42", Records(parsed.Debug));
        Assert.AreEqual(1L, ((Pointer)parsed.Debug[0].Value!).Address);
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
