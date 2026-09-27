namespace CStructSharp.Tests;

/// <summary>Checks constant dispatch labels across sibling and nested switch groups.</summary>
[TestClass]
public class SwitchConstantScopeBoundaryTests
{
    /// <summary>Each sibling switch keeps its own dispatch labels after all declarations have been normalized.</summary>
    [TestMethod]
    public void SiblingSwitches_RetainBothLabelTables()
    {
        var layout = new CStruct("struct root { uint8 tag; switch (tag) { case 1: { uint8 first; } } switch (tag) { case 1: { uint8 second; } } uint8 tail; };");
        byte[] bytes = [1, 42, 43, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");

        Assert.AreEqual((byte)42, (byte)parsed.first);
        Assert.AreEqual((byte)43, (byte)parsed.second);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>Nested switch normalization preserves outer constant labels even when input fields reuse their names.</summary>
    [TestMethod]
    public void NestedSwitch_PreservesInheritedConstants()
    {
        const string definition = "#define OUTER 1\n#define INNER 2\nstruct root { uint8 OUTER; uint8 INNER; uint8 tag; switch (tag) { case OUTER: { struct { uint8 selector; switch (selector) { case INNER: { uint8 chosen; } } } child; } } uint8 tail; };";
        var layout = new CStruct(definition);
        byte[] bytes = [9, 9, 1, 2, 42, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");

        Assert.AreEqual((byte)42, (byte)parsed.child.chosen);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }
}
