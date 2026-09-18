namespace CStructSharpTests;

using CStructSharp;

/// <summary>
///     A pointer-typed field occupies the pointer width, never the extent of the struct it points to. Address
///     resolution and updates traverse sibling fields after a pointer (and a self-referential pointer target) using
///     that width; measuring the pointee instead recursed until the nesting limit for `node *next` layouts.
/// </summary>
[TestClass]
public class PointerFieldExtentTests
{
    /// <summary>The field after a struct pointer starts one pointer width later, not one pointee size later.</summary>
    [TestMethod]
    public void FieldAfterStructPointer_StartsAfterThePointerWidth()
    {
        var layout = new CStruct("struct item { uint32 value; uint16 tag; }; struct root { item *target; uint8 after; };", pointerSize: 4);
        byte[] bytes = [5, 0, 0, 0, 0x7E, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66];
        using var stream = new MemoryStream(bytes, writable: true);

        Assert.AreEqual(4L, layout.ResolveAddress(stream, "root.after"));
        Assert.AreEqual(5L, layout.ResolveAddress(stream, "root.target.value"));

        layout.Update(stream, "root.after", (byte)0x99);
        Assert.AreEqual(0x99, bytes[4]);
        layout.Update(stream, "root.target.value.tag", (ushort)0xABCD);
        Assert.AreEqual(0xCD, bytes[9]);
        Assert.AreEqual(0xAB, bytes[10]);
    }

    /// <summary>Updating a scalar inside the target of a self-referential pointer does not recurse through the pointee type.</summary>
    [TestMethod]
    public void SelfReferentialPointerTarget_CanBeUpdated()
    {
        var layout = new CStruct("struct node { node *next; uint32 value; }; struct root { node *head; };", pointerSize: 4);
        byte[] bytes = [4, 0, 0, 0, 12, 0, 0, 0, 0xE8, 3, 0, 0, 0, 0, 0, 0, 0xE9, 3, 0, 0];
        using var stream = new MemoryStream(bytes, writable: true);

        layout.Update(stream, "root.head.value.value", 0xBEEFU);
        Assert.AreEqual(0xEF, bytes[8]);
        Assert.AreEqual(0xBE, bytes[9]);

        layout.Update(stream, "root.head.value.next.value.value", 0x1234U);
        Assert.AreEqual(0x34, bytes[16]);
        Assert.AreEqual(0x12, bytes[17]);

        dynamic parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(0xBEEFU, (uint)parsed.head.Value.value);
        Assert.AreEqual(0x1234U, (uint)parsed.head.Value.next.Value.value);
    }
}
