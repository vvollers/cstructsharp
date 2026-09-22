namespace CStructSharp.Tests;

/// <summary>Checks that named inline types remain available outside their containing declaration.</summary>
[TestClass]
public class HoistedCompositeTagTests
{
    /// <summary>Named inline types remain available even when their containing declaration is last in the layout.</summary>
    /// <param name="container">The declaration containing a named inline child.</param>
    [TestMethod]
    [DataRow("union container { struct child { uint8 value; } member; };")]
    [DataRow("typedef struct container { struct child { uint8 value; } member; } alias;")]
    [DataRow("struct container { union child { uint8 value; } member; };")]
    public void InlineTags_AreAvailableAtTheEndOfTheLayout(string container)
    {
        var layout = new CStruct(container);
        Assert.AreEqual(1, layout.GetStructSizeInBytes("child"));
        Assert.AreEqual((byte)7, layout.ReadValue<byte>(new byte[] { 7, }.AsSpan(), "child.value"));
    }

    /// <summary>Reparsing a promoted tagged body must not declare its nested tags twice.</summary>
    [TestMethod]
    public void PromotedTaggedBody_DeclaresNestedTagsExactlyOnce()
    {
        var layout = new CStruct("struct root { struct outer { struct inner { uint8 value; } item; }; };");
        Assert.AreEqual(1, layout.GetStructSizeInBytes("inner"));
        Assert.AreEqual(1, layout.GetStructSizeInBytes("outer"));
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual((byte)7, layout.ReadValue<byte>(new byte[] { 7, }.AsSpan(), "root.item.value"));
    }
}
