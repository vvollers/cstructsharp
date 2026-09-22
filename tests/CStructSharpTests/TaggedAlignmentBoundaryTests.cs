namespace CStructSharp.Tests;

/// <summary>Checks alignment annotations before tags and packing inheritance for named inline types.</summary>
[TestClass]
public class TaggedAlignmentBoundaryTests
{
    /// <summary>A hoisted type keeps its own explicit alignment or inherits the active packing cap.</summary>
    /// <param name="source">The declaration and any active packing directive.</param>
    /// <param name="alignment">The expected alignment cap of the named type.</param>
    /// <param name="size">The complete type extent in bytes.</param>
    /// <param name="offset">The value member's byte offset.</param>
    [TestMethod]
    [DataRow("#pragma pack(1)\nstruct @align(2) child { uint8 prefix; uint32 value; };", 2, 6, 2)]
    [DataRow("#pragma pack(1)\nstruct root { struct @align(2) child { uint8 prefix; uint32 value; } item; };", 2, 6, 2)]
    [DataRow("#pragma pack(1)\nstruct root { struct child { uint8 prefix; uint32 value; } item; };", 1, 5, 1)]
    [DataRow("struct root { struct child { uint8 prefix; uint32 value; } item; };", 4, 8, 4)]
    public void TaggedTypes_RetainTheirAlignment(string source, int alignment, int size, int offset)
    {
        var layout = new CStruct(source, aligned: true);
        Assert.AreEqual(alignment, layout.GetStructAlignmentInBytes("child"));
        Assert.AreEqual(size, layout.GetStructSizeInBytes("child"));
        using var stream = new MemoryStream(new byte[size]);
        Assert.AreEqual((long)offset, layout.ResolveAddress(stream, "child.value"));
    }
}
