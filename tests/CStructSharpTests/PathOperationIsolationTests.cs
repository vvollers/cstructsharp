namespace CStructSharpTests;

using System.Dynamic;
using CStructSharp;

/// <summary>
///     Verifies strict path interpretation and ensures targeted reads, writes, and address lookups inspect only the
///     selected layout branch while still enforcing pointer-safety limits.
/// </summary>
[TestClass]
public class PathOperationIsolationTests
{
    /// <summary>
    ///     value and address are ordinary byte fields in this record, at offsets 1 and 2.
    /// </summary>
    /// <remarks>
    ///     They acquire special meaning only after a pointer in a path. Invalid path strings must fail rather than
    ///     being partially accepted or misinterpreting those field names.
    /// </remarks>
    [TestMethod]
    public void Paths_AreStrictAndPointerAccessorNamesAreContextual()
    {
        const string layout = "struct root { byte first; byte value; byte address; byte tail; };";
        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream([0x01, 0x02, 0x03, 0x04,]);

        Assert.AreEqual(1L, cstruct.ResolveAddress(stream, "root.value"));
        stream.Position = 0;
        Assert.AreEqual(2L, cstruct.ResolveAddress(stream, "root.address"));

        foreach (string path in new[]
                 {
                     "root..value",
                     ".root.value",
                     "root.value.",
                     "root.value[0]junk",
                     "root.value[-1]",
                     "root.value[]",
                 })
        {
            stream.Position = 0;
            Assert.Throws<CStructPathException>(() => cstruct.ResolveAddress(stream, path), path);
        }
    }

    /// <summary>
    ///     The nested child has ordinary fields called value and address.
    /// </summary>
    /// <remarks>
    ///     Serializing either selected path must write that field's byte. These names must not trigger pointer logic
    ///     when the preceding item is a struct, allowing layouts to use common member names freely.
    /// </remarks>
    [TestMethod]
    public void SelectedWrites_TreatPointerAccessorNamesContextually()
    {
        const string layout = """
                              struct child { byte value; byte address; };
                              struct root { child item; };
                              """;
        var cstruct = new CStruct(layout);

        CollectionAssert.AreEqual(
            new byte[] { 0x2A, },
            cstruct.Serialize("root.item.value", (byte)0x2A));
        CollectionAssert.AreEqual(
            new byte[] { 0xA5, },
            cstruct.Serialize("root.item.address", (byte)0xA5));
    }

    /// <summary>
    ///     Two pointer hops lead to offset 4.
    /// </summary>
    /// <remarks>
    ///     A depth allowance of two succeeds, but disabling dereferencing or imposing smaller pointer limits must fail.
    ///     Asking only for an address must not bypass the same safety policy that applies when parsing the target
    ///     value.
    /// </remarks>
    [TestMethod]
    public void ResolveAddress_EnforcesPointerSafetyOptions()
    {
        var cstruct = new CStruct("struct root { byte** pointer; };", pointerSize: 2);
        using var stream = new MemoryStream(new byte[] { 0x02, 0x00, 0x04, 0x00, 0x2A, });

        Assert.AreEqual(
            4L,
            cstruct.ResolveAddress(
                stream,
                "root.pointer.value.value",
                options: new ReadOptions { MaxPointerDepth = 2, }));

        Assert.Throws<CStructPathException>(
            () => cstruct.ResolveAddress(
                stream,
                "root.pointer.value",
                options: new ReadOptions { DereferencePointers = false, }));
        Assert.Throws<CStructReadException>(
            () => cstruct.ResolveAddress(
                stream,
                "root.pointer.value.value",
                options: new ReadOptions { MaxPointerDepth = 1, }));
        Assert.Throws<CStructReadException>(
            () => cstruct.ResolveAddress(
                stream,
                "root.pointer.value",
                options: new ReadOptions { MaxPointerTargetBytes = 1, }));
    }

    /// <summary>
    ///     The record contains an invalid pointer before target and a missing field after it.
    /// </summary>
    /// <remarks>
    ///     Updating the existing target byte to 0xA5 must still succeed. Finding this field requires its position, not
    ///     dereferencing an unrelated pointer or validating absent data beyond the selected update.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_DoesNotReadUnrelatedPointerTargetsOrFollowingFields()
    {
        const string layout = "struct root { byte* bad; byte target; byte later; };";
        var cstruct = new CStruct(layout, pointerSize: 2);
        using var stream = new MemoryStream(new byte[] { 0xFF, 0x7F, 0x11, });

        cstruct.UpdateStream(stream, "root.target", (byte)0xA5);

        CollectionAssert.AreEqual(new byte[] { 0xFF, 0x7F, 0xA5, }, stream.ToArray());
    }

    /// <summary>
    ///     A selected child with value 0x2A can be read even though an unrelated pointer is invalid and a later field
    ///     is missing.
    /// </summary>
    /// <remarks>
    ///     Debug output must include only that child, and array-length lookup must similarly stop at the requested
    ///     branch. Selection limits which data is needed.
    /// </remarks>
    [TestMethod]
    public void SelectedReads_StopAfterTheRequestedLayoutBranch()
    {
        const string objectLayout = """
                                    struct child { byte value; };
                                    struct root { byte* bad; child selected; byte later; };
                                    """;
        var objectStruct = new CStruct(objectLayout, pointerSize: 2);
        using var objectStream = new MemoryStream(new byte[] { 0xFF, 0x7F, 0x2A, });

        dynamic selected = objectStruct.ParseStream(objectStream, "root.selected");
        Assert.AreEqual((byte)0x2A, (byte)selected.value);

        objectStream.Position = 0;
        (List<DebugData> debug, dynamic debugResult)
            = objectStruct.ParseStreamWithDebug(objectStream, "root.selected");
        dynamic selectedWithDebug = debugResult;
        Assert.AreEqual((byte)0x2A, (byte)selectedWithDebug.value);
        Assert.IsTrue(debug.All(item => item.DebugStackString.StartsWith("root.selected", StringComparison.Ordinal)));

        const string arrayLayout = "struct root { byte* bad; byte count; byte values[count]; byte later; };";
        var arrayStruct = new CStruct(arrayLayout, pointerSize: 2);
        using var arrayStream = new MemoryStream(new byte[] { 0xFF, 0x7F, 0x02, 0x11, 0x22, });
        Assert.AreEqual(2, arrayStruct.GetDynamicArrayLength(arrayStream, "root.values"));
    }
}
