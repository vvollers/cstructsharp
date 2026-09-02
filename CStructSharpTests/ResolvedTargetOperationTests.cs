namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies that path-based operations consume one semantic target instead of rebuilding layout state.</summary>
[TestClass]
public class ResolvedTargetOperationTests
{
    /// <summary>Preserves root-only address and update behavior for non-struct declarations.</summary>
    [TestMethod]
    public void RootTarget_TypedefRemainsAddressableAndWritable()
    {
        var cstruct = new CStruct("typedef uint16 word;", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x00, 0x00, });

        Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "word"));

        cstruct.UpdateStream(stream, "word", (ushort)0x1234);

        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, stream.ToArray());
    }

    /// <summary>
    ///     Combines alignment, a typedef, a nested fixed array, selected parsing, debug mapping, address lookup, and
    ///     update to prove every path operation reaches the same second-element field.
    /// </summary>
    [TestMethod]
    public void PathOperations_AlignedAliasArrayElement_Agree()
    {
        const string layout = """
                              typedef uint16 word;
                              struct item { uint8 prefix; word value; };
                              struct root { uint8 head; item items[2]; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, aligned: true);
        using var stream = new MemoryStream(
            new byte[] { 0x01, 0x00, 0x10, 0x00, 0x11, 0x11, 0x20, 0x00, 0x22, 0x22, });

        Assert.AreEqual(8L, cstruct.ResolveAddress(stream, "root.items[1].value"));

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.items[1]");
        Assert.AreEqual((byte)0x20, (byte)selected.prefix);
        Assert.AreEqual((ushort)0x2222, (ushort)selected.value);

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root.items[1]");
        Assert.IsTrue(
            debug.Any(item => item.CurPos == 8 && item.EndPos == 10 && item.DebugStackString == "root.items.value"));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.items[1].value", (ushort)0xABCD);

        CollectionAssert.AreEqual(
            new byte[] { 0x01, 0x00, 0x10, 0x00, 0x11, 0x11, 0x20, 0x00, 0xCD, 0xAB, },
            stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Preserves numeric bit positions in shared 16-bit storage for both layout byte orders.</summary>
    /// <param name="isLittleEndian">Whether the layout stores the least-significant byte first.</param>
    /// <param name="inputFirst">The first byte of the initial storage value.</param>
    /// <param name="inputSecond">The second byte of the initial storage value.</param>
    /// <param name="expectedFirst">The first byte expected after the update.</param>
    /// <param name="expectedSecond">The second byte expected after the update.</param>
    [TestMethod]
    [DataRow(true, (byte)0xD5, (byte)0xA5, (byte)0x55, (byte)0xA5)]
    [DataRow(false, (byte)0xA5, (byte)0xD5, (byte)0xA5, (byte)0x55)]
    public void PathOperations_MiddleBitfield_AgreeAcrossEndianness(
        bool isLittleEndian,
        byte inputFirst,
        byte inputSecond,
        byte expectedFirst,
        byte expectedSecond)
    {
        const string layout = "struct root { uint16 low:3; uint16 middle:5; uint16 high:8; };";
        var cstruct = new CStruct(layout, pointerSize: 1, isLittleEndian: isLittleEndian);
        using var stream = new MemoryStream(new byte[] { inputFirst, inputSecond, });

        Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.middle"));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.middle", (byte)0x0A);

        CollectionAssert.AreEqual(new byte[] { expectedFirst, expectedSecond, }, stream.ToArray());
        stream.Position = 0;
        dynamic parsed = cstruct.ParseStream(stream);
        Assert.AreEqual(0x05UL, Convert.ToUInt64(parsed.low));
        Assert.AreEqual(0x0AUL, Convert.ToUInt64(parsed.middle));
        Assert.AreEqual(0xA5UL, Convert.ToUInt64(parsed.high));
    }

    /// <summary>
    ///     Follows one pointer to a struct while treating the target's ordinary field named <c>value</c> contextually
    ///     across selected read, debug, address, and update operations.
    /// </summary>
    [TestMethod]
    public void PathOperations_PointerToStruct_AgreeAndKeepAccessorContext()
    {
        const string layout = """
                              struct child { uint8 value; };
                              struct root { child *ptr; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x2A, });

        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.ptr.value.value"));

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.ptr.value");
        Assert.AreEqual((byte)0x2A, (byte)selected.value);

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root.ptr.value");
        Assert.IsTrue(debug.Any(item => item.CurPos == 4 && item.DebugStackString == "root.ptr.value"));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.ptr.value.value", (byte)0xA5);

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x00, 0x00, 0x00, 0xA5, }, stream.ToArray());
    }

    /// <summary>Honors the update-specific pointer policy before a nested target can be changed.</summary>
    [TestMethod]
    public void UpdateStream_DisabledPointerDereferenceLeavesTargetUntouched()
    {
        const string layout = """
                              struct child { uint8 value; };
                              struct root { child *ptr; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x2A, });

        Assert.Throws<CStructPathException>(
            () => cstruct.UpdateStream(
                stream,
                "root.ptr.value.value",
                (byte)0xA5,
                options: new UpdateOptions { AllowPointerDereference = false, }));

        CollectionAssert.AreEqual(new byte[] { 0x04, 0x00, 0x00, 0x00, 0x2A, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Rejects object reads that stop on pointer storage before all declared pointer levels are consumed.</summary>
    [TestMethod]
    public void SelectedObjectRead_RequiresCompletePointerTraversal()
    {
        const string layout = """
                              struct child { uint8 value; };
                              struct root { child **ptr; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0x04, 0x00, 0x2A, });

        Assert.Throws<CStructPathException>(() => cstruct.ParseStream(stream, "root.ptr"));
        stream.Position = 0;
        Assert.Throws<CStructPathException>(() => cstruct.ParseStream(stream, "root.ptr.value"));

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.ptr.value.value");
        Assert.AreEqual((byte)0x2A, (byte)selected.value);
    }

    /// <summary>
    ///     Distinguishes the root pointer storage, the second-level pointer storage, and the final primitive target
    ///     while retaining the remaining depth for writes that intentionally stop between levels.
    /// </summary>
    [TestMethod]
    public void PointerLevelTargets_SelectTheRequestedStorage()
    {
        const string layout = "struct root { uint16 **ptr; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        byte[] bytes = [0x02, 0x00, 0x04, 0x00, 0x34, 0x12,];

        using var addressStream = new MemoryStream((byte[])bytes.Clone());
        Assert.AreEqual(0L, cstruct.ResolveAddress(addressStream, "root.ptr.address"));
        addressStream.Position = 0;
        Assert.AreEqual(2L, cstruct.ResolveAddress(addressStream, "root.ptr.value.address"));
        addressStream.Position = 0;
        Assert.AreEqual(4L, cstruct.ResolveAddress(addressStream, "root.ptr.value.value"));

        using var implicitStorageStream = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(implicitStorageStream, "root.ptr.value", (byte)0x05);
        CollectionAssert.AreEqual(
            new byte[] { 0x02, 0x00, 0x05, 0x00, 0x34, 0x12, },
            implicitStorageStream.ToArray());

        using var explicitStorageStream = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(explicitStorageStream, "root.ptr.value.address", (byte)0x05);
        CollectionAssert.AreEqual(implicitStorageStream.ToArray(), explicitStorageStream.ToArray());
    }

    /// <summary>Retains terminated-string target semantics after consuming the pointer's <c>.value</c> accessor.</summary>
    [TestMethod]
    public void UpdateStream_CharacterPointerTarget_UsesStringCodec()
    {
        const string layout = "struct root { char *name; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, });

        cstruct.UpdateStream(stream, "root.name.value", "hi");

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x00, (byte)'h', (byte)'i', 0x00, }, stream.ToArray());
    }

    /// <summary>Uses the same overlapping union-member address for selected parsing, debug data, and update.</summary>
    [TestMethod]
    public void PathOperations_UnionMember_Agree()
    {
        const string layout = """
                              union choice { uint8 small; uint16 large; };
                              struct root { uint8 head; choice value; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0xEE, 0x34, 0x12, });

        Assert.AreEqual(1L, cstruct.ResolveAddress(stream, "root.value.large"));

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.value");
        Assert.AreEqual((ushort)0x1234, (ushort)selected.large);

        stream.Position = 0;
        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root.value");
        Assert.IsTrue(debug.Any(item => item.CurPos == 1 && item.EndPos == 3));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.value.large", (ushort)0xABCD);

        CollectionAssert.AreEqual(new byte[] { 0xEE, 0xCD, 0xAB, }, stream.ToArray());
    }

    /// <summary>Resolves inline-struct fields without requiring a second declaration lookup during update.</summary>
    [TestMethod]
    public void PathOperations_InlineStruct_Agree()
    {
        const string layout = "struct root { struct { uint8 value; } child; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x2A, });

        Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.child.value"));

        stream.Position = 0;
        dynamic selected = cstruct.ParseStream(stream, "root.child");
        Assert.AreEqual((byte)0x2A, (byte)selected.value);

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.child.value", (byte)0xA5);

        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
    }

    /// <summary>Fails before mutation when a later bitfield's complete shared storage is not readable.</summary>
    [TestMethod]
    public void UpdateStream_TruncatedLaterBitfield_LeavesStreamUntouched()
    {
        const string layout = "struct root { uint16 low:4; uint16 high:4; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0xA5, });

        Assert.Throws<CStructReadException>(() => cstruct.UpdateStream(stream, "root.high", (byte)0x3));

        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }
}
