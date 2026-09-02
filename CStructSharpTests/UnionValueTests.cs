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
    ///     Exposes complete raw storage and every overlapping view without inventing a selected member.
    /// </summary>
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

    /// <summary>Rejects a truncated union before exposing incomplete storage or synthesized member views.</summary>
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

    /// <summary>Snapshots caller storage and member dictionaries instead of exposing mutable library-owned state.</summary>
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

    /// <summary>Uses an explicit member as authoritative and can return a parsed value to raw pass-through mode.</summary>
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

    /// <summary>Zero-fills new writes and applies the caller's clear/preserve policy to whole-union updates.</summary>
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

    /// <summary>Rejects malformed or legacy whole-union inputs before changing bytes or caller position.</summary>
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

    /// <summary>Refuses a preserving update when the destination lacks the complete existing union extent.</summary>
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

    /// <summary>Keeps outer expression variables authoritative while decoding or writing overlapping member views.</summary>
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

    /// <summary>Resets the expression context before each overlapping member is decoded.</summary>
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

    /// <summary>Counts a nested union as one structure level and releases that level between array elements.</summary>
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

    /// <summary>Retains one complete raw snapshot per array or nested union and advances by the compiled stride.</summary>
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
    ///     Reads an overlapping pointer address but does not traverse its external target until a path explicitly selects it.
    /// </summary>
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

    /// <summary>Adds one debug record for the full union extent alongside the existing overlapping member records.</summary>
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

    /// <summary>Allows a null selected scalar pointer while retaining the normal rejection for null non-pointer values.</summary>
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
