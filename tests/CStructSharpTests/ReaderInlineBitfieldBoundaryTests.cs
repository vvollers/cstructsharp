namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks that an inline member separates the bitfield runs on either side of it.</summary>
[TestClass]
public class ReaderInlineBitfieldBoundaryTests
{
    /// <summary>The bitfield after a named or promoted inline struct opens a new storage unit.</summary>
    /// <param name="named">Whether the inline struct has a member name instead of promoting its fields.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void InlineComposite_ClosesThePreviousBitfieldRun(bool named)
    {
        string suffix = named ? " child" : string.Empty;
        var layout = new CStruct("struct root { uint8 first : 3; struct { uint8 value; }" + suffix + "; uint8 last : 3; };");
        byte[] bytes = [5, 7, 6,];

        StructValue root = layout.ParseWithDebug(bytes.AsSpan(), "root").Value;

        StructValue child = named ? root.Get<StructValue>("child") : root;
        Assert.AreEqual(5, root.Get<int>("first"));
        Assert.AreEqual((byte)7, child.Get<byte>("value"));
        Assert.AreEqual(6, root.Get<int>("last"));
    }
}
