namespace CStructSharp.Tests;

using System.Numerics;

/// <summary>
///     The storage of an enum declared without a backing type: the compiler rule by default (32 bits, unsigned
///     unless a member is negative) or the spelling <see cref="CStructCompilationOptions.DefaultEnumStorage"/> names.
/// </summary>
[TestClass]
public class DefaultEnumStorageTests
{
    /// <summary>Non-negative members select <c>uint32</c> (so <c>0x80000000</c> fits); a negative literal selects <c>int32</c>.</summary>
    [TestMethod]
    public void CompilerRule_Selects32BitsAndSignednessFromTheMembers()
    {
        var layout = new CStruct("enum flags { NONE, TOP = 0x80000000 }; enum status { MISSING = -1, OK }; struct root { flags f; status s; };");
        Assert.AreEqual(8, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 0, 0, 0, 0x80, 0xFF, 0xFF, 0xFF, 0xFF, }.AsSpan(), "root");
        Assert.AreEqual("TOP", ((EnumValueResult)value.f).Name);
        Assert.AreEqual("uint32", ((EnumValueResult)value.f).StorageType);
        Assert.AreEqual("MISSING", ((EnumValueResult)value.s).Name);
        Assert.AreEqual("int32", ((EnumValueResult)value.s).StorageType);
        Assert.AreEqual(new BigInteger(-1), ((EnumValueResult)value.s).Value);
    }

    /// <summary>A named spelling pins the storage; it is a cache-key member and goes through every operation.</summary>
    [TestMethod]
    public void NamedStorage_RoundTripsThroughEveryOperation()
    {
        const string source = "enum kind { NONE, DATA = 2 }; struct root { kind type; uint8 tail; };";
        var options = new CStructCompilationOptions { DefaultEnumStorage = "uint8", };
        var layout = new CStruct(source, compilationOptions: options);
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(5, new CStruct(source).GetStructSizeInBytes("root"));
        Assert.AreNotSame(CStruct.GetOrCompile(source), CStruct.GetOrCompile(source, compilationOptions: options));

        byte[] bytes = [2, 7,];
        using var stream = new MemoryStream((byte[])bytes.Clone());
        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        Assert.IsTrue(debug.Any(item => item.DebugStackString == "root.type" && item.CurPos == 0 && item.EndPos == 1));
        stream.Position = 0;
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual("DATA", layout.ReadValue<EnumValueResult>(bytes.AsSpan(), "root.type").Name);

        CollectionAssert.AreEqual(bytes, layout.Serialize("root", new Dictionary<string, object?> { ["type"] = "DATA", ["tail"] = (byte)7, }));
        using var target = new MemoryStream();
        layout.WriteStream(target, "root", new Dictionary<string, object?> { ["type"] = 2, ["tail"] = (byte)7, });
        CollectionAssert.AreEqual(bytes, target.ToArray());
        layout.UpdateStream(stream, "root.type", "NONE");
        CollectionAssert.AreEqual(new byte[] { 0, 7, }, stream.ToArray());

        Assert.Throws<CStructLayoutException>(() => new CStruct("enum big { HUGE = 256 }; struct root { big b; };", compilationOptions: options));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct(source, compilationOptions: new CStructCompilationOptions { DefaultEnumStorage = " ", }));
    }
}
