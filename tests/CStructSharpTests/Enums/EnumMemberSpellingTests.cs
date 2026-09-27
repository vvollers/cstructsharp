namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Enum member names that C compilers would reject but Windows headers and dissect definitions use.</summary>
[TestClass]
public class EnumMemberSpellingTests
{
    /// <summary>A member may start with, or consist of, digits (a character-code enum names its members <c>0</c>, <c>1</c>, ...).</summary>
    [TestMethod]
    public void MemberName_MayStartWithADigit()
    {
        var layout = new CStruct("enum machine : uint16 { 32BIT_MACHINE = 0x0100, 64BIT = 0x0200, Plain = 1 }; struct root { machine m; };");
        dynamic value = layout.Parse(new byte[] { 0x00, 0x02, }.AsSpan(), "root");
        var result = (EnumValueResult)value.m;
        Assert.AreEqual("64BIT", result.Name);
        Assert.AreEqual(new BigInteger(0x0200), result.Value);

        byte[] written = layout.Serialize("root", new Dictionary<string, object?> { ["m"] = "32BIT_MACHINE", });
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x01, }, written);

        var digits = new CStruct("enum e : uint8 { 0 = 0x30, 1 = 0x31 }; struct root { e v; };");
        Assert.AreEqual("1", ((EnumValueResult)((dynamic)digits.Parse(new byte[] { 0x31, }.AsSpan(), "root")).v).Name);
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 32bit; };"));
    }

    /// <summary>Members may be separated by line breaks alone; a value can never be followed by a name, so the comma is optional.</summary>
    [TestMethod]
    public void Members_MayOmitTheComma()
    {
        var layout = new CStruct("flag F : uint16 {\n    NORMAL = 0x0000\n    FLUSH = 0x0001\n    LOST = 0x0002\n};\nenum E : uint8 { A B = 5 C }; struct root { F f; E e; };");
        dynamic value = layout.Parse(new byte[] { 0x03, 0x00, 6, }.AsSpan(), "root");
        CollectionAssert.AreEqual(new[] { "FLUSH", "LOST", }, ((FlagValueResult)value.f).Names);
        Assert.AreEqual("C", ((EnumValueResult)value.e).Name);
        Assert.Throws<CStructLayoutException>(() => new CStruct("enum e : uint8 { A = }; struct root { e v; };"));
    }
}
