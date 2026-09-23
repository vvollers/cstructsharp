namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks selector validation and one shared decision across each conditional group's members.</summary>
[TestClass]
public class ConditionalGroupBoundaryTests
{
    /// <summary>An inline child cannot make later members reevaluate the enclosing branch after changing a variable.</summary>
    [TestMethod]
    public void InlineChild_PreservesTheOuterGroupDecision()
    {
        var layout = new CStruct("#define flag 1\nstruct root { if (flag) { struct { uint8 flag; } child; uint8 chosen; } uint8 tail; };");
        byte[] bytes = [0, 42, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.chosen);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
        Assert.AreEqual(2L, layout.ResolveAddress(bytes, "root.tail"));
    }

    /// <summary>A default-only dispatch still captures its input selector before reading or writing the selected member.</summary>
    [TestMethod]
    public void DefaultOnlySwitch_CapturesItsSelector()
    {
        var layout = new CStruct("struct root { uint8 tag; switch (tag) { default: { uint8 value; } } uint8 tail; };");
        byte[] bytes = [3, 42, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.value);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>An unsupported selector call cannot hide behind an unconditional default arm.</summary>
    [TestMethod]
    public void DefaultOnlySwitch_ValidatesItsSelectorAtConstruction()
    {
        // Selecting the default arm does not make an invalid selector expression part of the supported language.
        Assert.Throws<CStructLayoutException>(() =>
            new CStruct("struct root { switch (unsupported(1)) { default: { uint8 value; } } };"));
    }
}
