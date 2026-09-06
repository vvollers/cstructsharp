namespace CStructSharp.Tests;

using System.Collections;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using Microsoft.CSharp.RuntimeBinder;

/// <summary>Verifies the explicit, byte-exact value model shared by every union operation.</summary>
[TestClass]
public class UnionValueTests
{
    /// <summary>
    ///     small and large describe the same two bytes as different integer widths.
    /// </summary>
    /// <remarks>
    ///     Parsing must retain all raw bytes and both interpretations without guessing which member is active.
    ///     Serializing that unedited UnionValue must reproduce the original storage in either byte order and alignment
    ///     mode.
    /// </remarks>
    /// <param name="aligned">Whether the layout applies portable field alignment.</param>
    /// <param name="isLittleEndian">Whether the wider overlapping member stores its low byte first.</param>
    [TestMethod]
    [DynamicData(nameof(RegressionTestSupport.AlignmentAndEndianMatrix), typeof(RegressionTestSupport))]
    public void ParseStream_UnionValueRetainsExactStorageAndViews(bool aligned, bool isLittleEndian)
    {
        var cstruct = new CStruct(
            "union choice { uint8 small; uint16 large; };",
            pointerSize: 1,
            aligned: aligned,
            isLittleEndian: isLittleEndian);
        var bytes = new byte[2];
        RegressionTestSupport.WriteUnsigned(bytes, 0, 2, 0x1234, isLittleEndian);
        using var stream = new MemoryStream(bytes);

        var parsed = (UnionValue)cstruct.ParseStream(stream, "choice");
        dynamic dynamicParsed = parsed;

        Assert.AreEqual("choice", parsed.UnionName);
        Assert.IsTrue(parsed.HasRawStorage);
        Assert.IsFalse(parsed.HasSelection);
        Assert.IsNull(parsed.SelectedMember);
        Assert.IsNull(parsed.SelectedValue);
        CollectionAssert.AreEqual(bytes, parsed.RawStorage!.Value.ToArray());
        Assert.AreEqual(bytes[0], (byte)parsed["small"]!);
        Assert.AreEqual((ushort)0x1234, (ushort)dynamicParsed.large);
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("choice", parsed));
        Assert.AreEqual(bytes.Length, stream.Position);
    }

    /// <summary>
    ///     Although small uses one byte, large makes the union two bytes wide.
    /// </summary>
    /// <remarks>
    ///     Supplying only 34 is therefore incomplete and must cause a read error. The library must not accept the short
    ///     member and invent missing bytes for the wider view.
    /// </remarks>
    [TestMethod]
    public void ParseStream_TruncatedUnionFailsWithReadException()
    {
        var cstruct = new CStruct("union choice { uint8 small; uint16 large; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x34, });

        CStructReadException exception = Assert.Throws<CStructReadException>(
            () => cstruct.ParseStream(stream, "choice"));

        StringAssert.Contains(exception.Message, "Not enough bytes");
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>
    ///     After constructing a UnionValue, the test changes both the original byte array and an exposed copy.
    /// </summary>
    /// <remarks>
    ///     The value must still retain 34 12, and its member dictionary must reject writes. These ownership rules keep
    ///     a previously parsed result stable when caller-owned data changes.
    /// </remarks>
    [TestMethod]
    public void UnionValue_SnapshotsRawStorageAndExposesReadOnlyMembers()
    {
        byte[] source = [0x34, 0x12,];
        UnionValue raw = UnionValue.FromRaw("choice", source);
        source[0] = 0xFF;

        ReadOnlyMemory<byte> exposed = raw.RawStorage!.Value;
        Assert.IsTrue(MemoryMarshal.TryGetArray(exposed, out ArraySegment<byte> exposedArray));
        exposedArray.Array![exposedArray.Offset] = 0xEE;

        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, raw.RawStorage!.Value.ToArray());
        Assert.Throws<NotSupportedException>(
            () => ((IDictionary<string, object?>)raw.Members).Add("small", (byte)1));

        UnionValue selected = UnionValue.FromMember("choice", "small", (byte)1);
        Assert.IsNull(selected.RawStorage);
        Assert.IsInstanceOfType<ReadOnlyDictionary<string, object?>>(selected.Members);
    }

    /// <summary>
    ///     Selecting small = 0xA5 creates an edited value without changing the original parsed union or its saved 34 12
    ///     bytes.
    /// </summary>
    /// <remarks>
    ///     Removing the selection restores raw-storage mode. Other member views remain available, but they are not
    ///     automatically recomputed previews of the selected replacement.
    /// </remarks>
    [TestMethod]
    public void UnionValue_SelectionCanBeAddedAndRemovedWithoutLosingRawStorage()
    {
        var cstruct = new CStruct("union choice { uint8 small; uint16 large; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x34, 0x12, });
        var parsed = (UnionValue)cstruct.ParseStream(stream, "choice");
        UnionValue edited = parsed.WithSelectedMember("small", (byte)0xA5);
        UnionValue restored = edited.WithoutSelection();

        Assert.IsTrue(edited.HasSelection);
        Assert.IsTrue(edited.HasRawStorage);
        Assert.AreEqual("small", edited.SelectedMember);
        Assert.AreEqual((byte)0xA5, (byte)edited.SelectedValue!);
        Assert.AreEqual((byte)0xA5, (byte)edited["small"]!);
        Assert.AreEqual((byte)0x34, (byte)parsed["small"]!, "The original parsed view remains unchanged.");
        Assert.AreEqual(2, edited.Count);
        CollectionAssert.AreEqual(new[] { "small", "large", }, edited.Keys.ToArray());
        Assert.AreEqual(2, edited.ToArray().Length);
        Assert.AreEqual(2, ((IEnumerable)edited).Cast<object>().Count());
        Assert.IsTrue(edited.ContainsKey("large"));
        Assert.IsTrue(edited.TryGetValue("large", out object? large));
        Assert.AreEqual((ushort)0x1234, (ushort)large!);
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, edited.RawStorage!.Value.ToArray());
        Assert.IsFalse(restored.HasSelection);
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, restored.RawStorage!.Value.ToArray());
        dynamic dynamicEdited = edited;
        Assert.AreEqual((ushort)0x1234, (ushort)dynamicEdited.large);
        Assert.Throws<RuntimeBinderException>(() => _ = dynamicEdited.missing);
        Assert.Throws<InvalidOperationException>(
            () => UnionValue.FromMember("choice", "small", (byte)1).WithoutSelection());
        Assert.Throws<ArgumentException>(() => UnionValue.FromRaw(" ", new byte[] { 1, }));
        Assert.Throws<ArgumentException>(() => UnionValue.FromMember("choice", " ", (byte)1));
        Assert.Throws<ArgumentException>(() => parsed.WithSelectedMember(" ", (byte)1));

        UnionValue selectedFromRaw = UnionValue.FromRaw("choice", new byte[] { 0x34, 0x12, })
            .WithSelectedMember("small", (byte)1);
        Assert.AreEqual(1, selectedFromRaw.Count);
        Assert.AreEqual((byte)1, (byte)selectedFromRaw["small"]!);
    }

    /// <summary>
    ///     Writing a selected one-byte member still produces a two-byte union.
    /// </summary>
    /// <remarks>
    ///     New serialization and default whole-union updates clear the remaining byte, giving A5 00. With
    ///     ClearUnionStorage disabled, an update preserves the old second byte and gives A5 12; a raw-storage value
    ///     replaces both bytes exactly.
    /// </remarks>
    [TestMethod]
    public void ExplicitMemberWrite_UsesCompleteUnionStoragePolicy()
    {
        var cstruct = new CStruct("union choice { uint16 wide; uint8 small; };", pointerSize: 1);
        UnionValue selected = UnionValue.FromMember("choice", "small", (byte)0xA5);

        CollectionAssert.AreEqual(
            new byte[] { 0xA5, 0x00, },
            cstruct.Serialize("choice", selected));

        using var clearing = new MemoryStream(new byte[] { 0x34, 0x12, });
        cstruct.UpdateStream(clearing, "choice", selected);
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0x00, }, clearing.ToArray());

        using var directWrite = new MemoryStream(new byte[] { 0x34, 0x12, });
        cstruct.WriteStream(directWrite, "choice", selected);
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0x00, }, directWrite.ToArray());

        using var preserving = new MemoryStream(new byte[] { 0x34, 0x12, });
        cstruct.UpdateStream(
            preserving,
            "choice",
            selected,
            options: new UpdateOptions { ClearUnionStorage = false, });
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0x12, }, preserving.ToArray());

        using var raw = new MemoryStream(new byte[] { 0x34, 0x12, });
        cstruct.UpdateStream(
            raw,
            "choice",
            UnionValue.FromRaw("choice", new byte[] { 0xFE, 0xDC, }),
            options: new UpdateOptions { ClearUnionStorage = false, });
        CollectionAssert.AreEqual(new byte[] { 0xFE, 0xDC, }, raw.ToArray());
    }

    /// <summary>
    ///     A whole-union write needs a valid UnionValue with the correct type, storage length, and selected member.
    /// </summary>
    /// <remarks>
    ///     Plain dictionaries, wrong names, wrong byte counts, and invalid member payloads must fail. The original
    ///     bytes and caller position must remain intact after every rejected update.
    /// </remarks>
    [TestMethod]
    public void WholeUnionWrite_InvalidValueFailsBeforeMutation()
    {
        var cstruct = new CStruct("union choice { uint16 wide; uint8 small; };", pointerSize: 1);
        object[] invalidValues =
        [
            new Dictionary<string, object?> { ["small"] = (byte)1, },
            UnionValue.FromRaw("choice", new byte[] { 1, }),
            UnionValue.FromRaw("other", new byte[] { 1, 2, }),
            UnionValue.FromMember("choice", "missing", (byte)1),
            UnionValue.FromRaw("choice", new byte[] { 1, }).WithSelectedMember("small", (byte)1),
            UnionValue.FromMember("choice", "wide", "not-a-number"),
        ];

        foreach (object invalid in invalidValues)
        {
            byte[] original = [0x34, 0x12,];
            using var stream = new MemoryStream((byte[])original.Clone()) { Position = 1, };

            Assert.Throws<CStructWriteException>(
                () => cstruct.UpdateStream(stream, "choice", invalid),
                invalid.GetType().Name);
            CollectionAssert.AreEqual(original, stream.ToArray(), invalid.GetType().Name);
            Assert.AreEqual(1L, stream.Position, invalid.GetType().Name);
        }

        var arrayUnion = new CStruct("union choice { uint8 values[2]; uint16 wide; };", pointerSize: 1);
        using var arrayStream = new MemoryStream(new byte[] { 0x34, 0x12, });
        Assert.Throws<CStructWriteException>(
            () => arrayUnion.UpdateStream(
                arrayStream,
                "choice",
                UnionValue.FromMember("choice", "values", (byte)1)));
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, arrayStream.ToArray());
    }

    /// <summary>
    ///     Preserving unused union bytes requires the entire old two-byte union to exist.
    /// </summary>
    /// <remarks>
    ///     A one-byte destination is insufficient even when the selected replacement is only one byte wide. The read
    ///     error must leave that byte and the original stream position unchanged.
    /// </remarks>
    [TestMethod]
    public void WholeUnionUpdate_PreservePolicyRequiresCompleteExistingStorage()
    {
        var cstruct = new CStruct("union choice { uint16 wide; uint8 small; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x34, });

        Assert.Throws<CStructReadException>(
            () => cstruct.UpdateStream(
                stream,
                "choice",
                UnionValue.FromMember("choice", "small", (byte)0xA5),
                options: new UpdateOptions { ClearUnionStorage = false, }));

        CollectionAssert.AreEqual(new byte[] { 0x34, }, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>
    ///     The outer count is 1, while an overlapping union member also named count reads 3. items[count] belongs to
    ///     the outer record and must still contain one element.
    /// </summary>
    /// <remarks>
    ///     Reading or writing union views must not overwrite the parent's variables and change the following array
    ///     length.
    /// </remarks>
    [TestMethod]
    public void UnionMembers_DoNotLeakNamesIntoContainingExpressionScope()
    {
        const string layout = """
                              union choice { uint16 wide; uint8 count; };
                              struct root { uint8 count; choice value; uint8 items[count]; };
                              """;
        byte[] bytes = [0x01, 0x03, 0x00, 0xA5,];
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(1, ((List<object?>)parsed.items).Count);
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     The define count = 1 determines data[count].
    /// </summary>
    /// <remarks>
    ///     Reading a different union member named count as 3 must not change that array into three elements. Each
    ///     overlapping view starts with the same expression context, so the union consumes only its one declared byte.
    /// </remarks>
    [TestMethod]
    public void UnionMemberViews_DoNotInfluenceFollowingMemberArrayLengths()
    {
        const string layout = """
                              #define count 1
                              union choice { uint8 count; uint8 data[count]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x03, 0xA5, 0xA5, });

        var parsed = (UnionValue)cstruct.ParseStream(stream, "choice");

        Assert.AreEqual(1, ((List<object?>)parsed["data"]!).Count);
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>
    ///     root contains two union elements side by side.
    /// </summary>
    /// <remarks>
    ///     Each union adds one active nesting level, so a limit that is too shallow fails, while a sufficient limit
    ///     reads both. Finishing the first element must release its level instead of making the second appear more
    ///     deeply nested.
    /// </remarks>
    [TestMethod]
    public void UnionRead_NestingBudgetTracksAndReleasesEachElement()
    {
        const string layout = """
                              union choice { uint8 value; };
                              struct root { choice values[2]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);

        Assert.Throws<CStructReadLimitException>(
            () => cstruct.ParseStream(
                new MemoryStream(new byte[] { 1, 2, }),
                "root",
                new Dictionary<string, CStructSharp.Structure.Expr>(),
                new ReadOptions { MaxNestingDepth = 1, }));

        dynamic parsed = cstruct.ParseStream(
            new MemoryStream(new byte[] { 1, 2, }),
            "root",
            new Dictionary<string, CStructSharp.Structure.Expr>(),
            new ReadOptions { MaxNestingDepth = 2, });
        Assert.AreEqual(2, ((List<object?>)parsed.values).Count);
    }

    /// <summary>
    ///     Each choice union occupies two bytes.
    /// </summary>
    /// <remarks>
    ///     The two array elements must save 11 22 and 33 44, and the nested union must save 55 66. The next byte is
    ///     tail = 0x7E. Round-trip equality detects any attempt to advance by only the smallest member's size.
    /// </remarks>
    [TestMethod]
    public void ParseStream_UnionArraysAndNestedUnionsUseCompleteStride()
    {
        const string layout = """
                              union choice { uint16 wide; uint8 small; };
                              struct inner { choice value; };
                              struct root { choice values[2]; inner nested; uint8 tail; };
                              """;
        byte[] bytes = [0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x7E,];
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        var values = (List<object?>)parsed.values;
        var first = (UnionValue)values[0]!;
        var second = (UnionValue)values[1]!;
        var nested = (UnionValue)parsed.nested.value;

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, }, first.RawStorage!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x33, 0x44, }, second.RawStorage!.Value.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x55, 0x66, }, nested.RawStorage!.Value.ToArray());
        Assert.AreEqual((byte)0x7E, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     The same union bytes can represent an integer or a pointer, and the data does not say which view is active.
    /// </summary>
    /// <remarks>
    ///     Parsing therefore exposes address 2 without following it. An explicit path to target.value may resolve
    ///     offset 2, making pointer traversal a deliberate selection rather than a guess.
    /// </remarks>
    [TestMethod]
    public void ParseStream_UnselectedUnionPointerViewDoesNotDereferenceTarget()
    {
        const string layout = """
                              union choice { uint8 *target; uint16 word; };
                              struct root { choice value; uint8 targetByte; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0xA5, });

        dynamic parsed = cstruct.ParseStream(stream, "root");
        var union = (UnionValue)parsed.value;
        var pointer = (Pointer)union["target"]!;

        Assert.AreEqual(2L, pointer.Address);
        Assert.IsFalse(pointer.IsDereferenced);
        Assert.IsNull(pointer.Value);
        stream.Position = 0;
        Assert.AreEqual(2L, cstruct.ResolveAddress(stream, "root.value.target.value"));
    }

    /// <summary>
    ///     Debug output must include a union-level range covering offsets 0 through 2 with bytes 34 12, alongside the
    ///     overlapping member ranges.
    /// </summary>
    /// <remarks>
    ///     Several records can start at zero because union views share storage. The full-range record lets an inspector
    ///     represent the union's complete extent.
    /// </remarks>
    [TestMethod]
    public void ParseStreamWithDebug_RecordsCompleteUnionStorage()
    {
        const string layout = "union choice { uint8 small; uint16 large; }; struct root { choice value; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x34, 0x12, });

        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");
        DebugData storage = debug.Single(item => item.Value is UnionValue);

        Assert.AreEqual(0L, storage.CurPos);
        Assert.AreEqual(2L, storage.EndPos);
        Assert.AreEqual("choice", storage.TypeName);
        CollectionAssert.AreEqual(new[] { 0x34, 0x12, }, storage.Buffer);
        Assert.IsTrue(debug.Count(item => item.CurPos == 0) >= 3);
    }

    /// <summary>
    ///     Selecting the pointer member with null must write a zero address, producing two zero bytes.
    /// </summary>
    /// <remarks>
    ///     Selecting the ordinary uint16 value member with null must fail because that field needs a number. A union
    ///     selection preserves the normal null rules of the member being written.
    /// </remarks>
    [TestMethod]
    public void ExplicitMemberWrite_NullFollowsScalarPointerRules()
    {
        var cstruct = new CStruct("union choice { uint16 *pointer; uint16 value; };", pointerSize: 2);

        CollectionAssert.AreEqual(
            new byte[] { 0x00, 0x00, },
            cstruct.Serialize("choice", UnionValue.FromMember("choice", "pointer", null)));
        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize("choice", UnionValue.FromMember("choice", "value", null)));
    }
}
