namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Top-level composite spellings beyond <c>struct Name { };</c>: anonymous bodies with a trailing type name, object declarations, and forward declarations.</summary>
[TestClass]
public class TopLevelDeclarationTests
{
    /// <summary><c>struct { ... } name;</c> declares the type <c>name</c> (the dissect spelling of a typedef).</summary>
    [TestMethod]
    public void AnonymousBody_TrailingNameIsTheType()
    {
        var layout = new CStruct("struct { uint32 tv_sec; uint32 tv_usec; } timeval; union { uint8 a; uint16 b; } choice; struct root { timeval t; choice c; };");
        Assert.AreEqual(10, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, }.AsSpan(), "root");
        Assert.AreEqual(2U, (uint)value.t.tv_usec);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("timeval"));
        Assert.IsTrue(layout.CStructElements["choice"] is CStructSharp.Syntax.Struct { IsUnion: true, });
    }

    /// <summary><c>struct X { ... } variable;</c> declares the type; the variable name is not a declaration.</summary>
    [TestMethod]
    public void NamedBody_TrailingVariableIsIgnored()
    {
        var layout = new CStruct("struct timeval { uint32 s; } header; struct root { timeval t; };");
        Assert.AreEqual(4, layout.GetStructSizeInBytes("root"));
        Assert.IsFalse(layout.CStructElements.ContainsKey("header"));

        // The variable does not reserve its name, and the type is still required to be unique.
        Assert.AreEqual(1, new CStruct("struct a { uint8 x; } header; struct header { uint8 y; };").GetStructSizeInBytes("header"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct a { uint8 x; } v; struct a { uint8 y; };"));
    }

    /// <summary>A type declared by an anonymous body takes part in every operation like any named struct.</summary>
    [TestMethod]
    public void AnonymousBody_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct("struct { uint16 sec; uint16 usec; } timeval; struct root { timeval stamp; uint8 tail; } header;");
        byte[] bytes = [1, 0, 2, 0, 3,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        stream.Position = 0;
        dynamic parsed = layout.ParseStream(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.stamp.usec" && item.Start == 2 && item.End == 4));
        Assert.AreEqual(2, layout.ResolveAddress(stream, "root.stamp.usec"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        using var written = new MemoryStream();
        layout.WriteStream(written, "timeval", new Dictionary<string, object?> { ["sec"] = (ushort)1, ["usec"] = (ushort)2, });
        CollectionAssert.AreEqual(new byte[] { 1, 0, 2, 0, }, written.ToArray());

        layout.UpdateStream(stream, "root.stamp.sec", (ushort)0x0A0B);
        CollectionAssert.AreEqual(new byte[] { 0x0B, 0x0A, 2, 0, 3, }, stream.ToArray());
        Assert.AreEqual((ushort)0x0A0B, layout.ReadValue<ushort>(stream.ToArray().AsSpan(), "root.stamp.sec"));
    }

    /// <summary>A forward declaration declares nothing; the tag must still be defined before use.</summary>
    [TestMethod]
    public void ForwardDeclaration_IsAccepted()
    {
        var layout = new CStruct("struct node; union u; struct node { uint32 v; node *next; }; union u { uint8 a; };");
        Assert.AreEqual(12, layout.GetStructSizeInBytes("node"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct node; struct root { node n; };"));
    }
}
