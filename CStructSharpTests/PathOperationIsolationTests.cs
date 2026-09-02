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
    ///     Rejects partially valid path strings and proves that ordinary fields called value/address retain their literal
    ///     meaning rather than being globally treated as pointer helper names.
    /// </summary>
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
    ///     Keeps the names <c>value</c> and <c>address</c> available to ordinary nested fields during selected writes;
    ///     they become pointer accessors only when the preceding declaration is actually a pointer.
    /// </summary>
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
    ///     Applies the same dereference, depth, and target-size safety policy to targeted address resolution that full
    ///     parsing uses, so a narrow path API cannot become a way around caller-defined pointer budgets.
    /// </summary>
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
    ///     Resolves only the selected update path: an unrelated invalid pointer and an absent later field must not block
    ///     changing an already-present scalar byte.
    /// </summary>
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
    ///     Applies prefix-only traversal to selected object parsing, debug mapping, and dynamic length lookup so all
    ///     path-based reads remain independent from invalid pointer targets and absent trailing siblings.
    /// </summary>
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
