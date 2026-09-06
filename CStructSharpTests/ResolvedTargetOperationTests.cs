namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies that path-based operations consume one semantic target instead of rebuilding layout state.</summary>
[TestClass]
public class ResolvedTargetOperationTests
{
    /// <summary>
    ///     word aliases uint16 and is itself a valid root selection.
    /// </summary>
    /// <remarks>
    ///     Its address is zero, and updating it to 0x1234 must produce 34 12. Path operations must support scalar alias
    ///     roots instead of assuming every root is a struct with members.
    /// </remarks>
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
    ///     word is a uint16 alias inside an aligned item array.
    /// </summary>
    /// <remarks>
    ///     The second item's value must be at offset 8 and read 0x2222. Selected parsing, debug ranges, and updates
    ///     must combine the alias width, record padding, and array stride consistently.
    /// </remarks>
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

    /// <summary>
    ///     The middle five-bit slice of 0xA5D5 shares its byte address with low and high.
    /// </summary>
    /// <remarks>
    ///     Replacing middle with 10 must produce storage value 0xA555 in either byte order. Re-parsing must still
    ///     return low = 5 and high = 0xA5, proving only the selected bits changed.
    /// </remarks>
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
    ///     In ptr.value.value, the first value follows the pointer and the second names the child's ordinary byte
    ///     field.
    /// </summary>
    /// <remarks>
    ///     Both must resolve to offset 4 and read 0x2A. Updating that field to 0xA5 must preserve the address and
    ///     intervening bytes.
    /// </remarks>
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

    /// <summary>
    ///     The pointer leads to an existing child at offset 4, but UpdateOptions forbids following pointers.
    /// </summary>
    /// <remarks>
    ///     The selected update must raise a path error and leave the child value 0x2A unchanged. A valid target address
    ///     does not override the caller's traversal policy.
    /// </remarks>
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

    /// <summary>
    ///     child** requires two .value steps to reach a child object.
    /// </summary>
    /// <remarks>
    ///     ParseStream must reject selections that stop on either pointer slot and accept the complete path, returning
    ///     value = 0x2A. Object parsing must not confuse remaining pointer storage with the final struct.
    /// </remarks>
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
    ///     The outer pointer slot is at 0, the intermediate slot at 2, and the uint16 target at 4.
    /// </summary>
    /// <remarks>
    ///     Address lookup and updates must distinguish these three locations. Stopping at an intermediate pointer
    ///     retains pointer-writing behavior; only the final .value uses the uint16 writer.
    /// </remarks>
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

    /// <summary>
    ///     name points to offset 2.
    /// </summary>
    /// <remarks>
    ///     Selecting name.value with replacement hi must write h, i, and a zero terminator there, preserving the
    ///     pointer byte. After following a character pointer, the target is terminated text rather than a single char
    ///     field.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_CharacterPointerTarget_UsesStringCodec()
    {
        const string layout = "struct root { char *name; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, });

        cstruct.UpdateStream(stream, "root.name.value", "hi");

        CollectionAssert.AreEqual(new byte[] { 0x02, 0x00, (byte)'h', (byte)'i', 0x00, }, stream.ToArray());
    }

    /// <summary>
    ///     The union follows a one-byte head, so large starts at offset 1 and initially reads 0x1234.
    /// </summary>
    /// <remarks>
    ///     Debug information must cover bytes 1 through 3. Updating large to 0xABCD must produce EE CD AB without
    ///     shifting the overlapping union storage.
    /// </remarks>
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

    /// <summary>
    ///     child is declared inline and has no separate reusable type name.
    /// </summary>
    /// <remarks>
    ///     Its value still resolves at offset zero, reads 0x2A, and updates to 0xA5. Operations must use the actual
    ///     compiled child declaration instead of trying a global lookup by its field name.
    /// </remarks>
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

    /// <summary>
    ///     high uses bits in a uint16 unit, even though both declared slices fit within the supplied first byte.
    /// </summary>
    /// <remarks>
    ///     The complete two-byte backing storage is required for safe preservation. Updating high must therefore fail
    ///     and leave A5 and position zero unchanged.
    /// </remarks>
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
