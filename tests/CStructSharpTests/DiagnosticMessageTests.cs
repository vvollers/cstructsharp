namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Golden messages for the failures a first-time user meets most. Each message says what failed, on which
///     field and type, in which requested path, and where the operation stopped, so a caller that only logs
///     <see cref="Exception.Message"/> can act on it.
/// </summary>
[TestClass]
public class DiagnosticMessageTests
{
    private static readonly CStruct Header = new("struct header { uint16 kind; uint32 length; };");

    /// <summary>A truncated input names the field that needed the bytes and how many were left.</summary>
    [TestMethod]
    public void TruncatedInput_NamesFieldNeededAndAvailable()
    {
        CStructReadException exception = Assert.Throws<CStructReadException>(() => Header.Parse(new byte[] { 2, 0, 6 }, "header"));

        Assert.AreEqual("Not enough bytes: needed 4, available 1 (field 'length' (uint32), in 'header', offset 3).", exception.Message);
        Assert.AreEqual("length", exception.Member);
        Assert.AreEqual("uint32", exception.MemberType);
        Assert.AreEqual("header", exception.Path);
        Assert.AreEqual(3, exception.Offset);
    }

    /// <summary>The innermost field of a nested struct is the one reported.</summary>
    [TestMethod]
    public void NestedTruncation_ReportsTheInnermostField()
    {
        var nested = new CStruct("struct inner { uint8 a; uint32 b; }; struct outer { uint8 x; inner i; uint16 y; };");

        CStructReadException exception = Assert.Throws<CStructReadException>(() => nested.Parse(new byte[] { 1, 2, 3 }, "outer"));

        Assert.AreEqual("b", exception.Member);
        StringAssert.StartsWith(exception.Message, "Not enough bytes: needed 4, available 1 (field 'b' (uint32)");
    }

    /// <summary>A value outside the field's range says what the field accepts.</summary>
    [TestMethod]
    public void ValueOutOfRange_StatesTheAcceptedRange()
    {
        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => Header.Serialize("header", new Dictionary<string, object?> { ["kind"] = 70000, ["length"] = 6 }));

        Assert.AreEqual("Value 70000 does not fit: uint16 accepts 0 to 65535 (field 'kind' (uint16), in 'header', offset 0).", exception.Message);
    }

    /// <summary>A value of the wrong kind is shown as written.</summary>
    [TestMethod]
    public void WrongValueKind_ShowsTheValue()
    {
        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => Header.Serialize("header", new Dictionary<string, object?> { ["kind"] = "abc", ["length"] = 6 }));

        StringAssert.StartsWith(exception.Message, "Value \"abc\" cannot be written as uint16 (field 'kind' (uint16)");
    }

    /// <summary>A missing member is named.</summary>
    [TestMethod]
    public void MissingMember_IsNamed()
    {
        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => Header.Serialize("header", new Dictionary<string, object?> { ["kind"] = 1 }));

        StringAssert.StartsWith(exception.Message, "No value was supplied for 'length' (field 'length' (uint32)");
    }

    /// <summary>A root that differs only in case is suggested; otherwise the declared roots are listed.</summary>
    [TestMethod]
    public void UnknownRoot_SuggestsTheDeclaredName()
    {
        byte[] bytes = [2, 0, 6, 0, 0, 0,];

        CStructPathException typo = Assert.Throws<CStructPathException>(() => Header.Parse(bytes, "Header"));
        Assert.AreEqual("Unknown root 'Header'. Names are case-sensitive; did you mean 'header'?", typo.Message);

        CStructPathException other = Assert.Throws<CStructPathException>(() => Header.Parse(bytes, "packet"));
        Assert.AreEqual("Unknown root 'packet'. The layout declares: header.", other.Message);
    }

    /// <summary>An unknown member names the containing struct and the requested path.</summary>
    [TestMethod]
    public void UnknownMember_NamesTheStructAndPath()
    {
        CStructPathException exception = Assert.Throws<CStructPathException>(() => Header.ReadValue(new byte[] { 2, 0, 6, 0, 0, 0, }, "header.nope"));

        Assert.AreEqual("Unknown field 'nope' in 'header' (path 'header.nope', offset 6).", exception.Message);
    }

    /// <summary>An array count over the limit shows the count and the limit.</summary>
    [TestMethod]
    public void ArrayLimit_ShowsCountAndLimit()
    {
        var layout = new CStruct("struct p { uint32 n; uint8 data[n]; };");

        CStructReadLimitException exception = Assert.Throws<CStructReadLimitException>(() => layout.Parse(new byte[] { 0xFF, 0xFF, 0xFF, 0x7F, 1, }, "p"));

        StringAssert.StartsWith(exception.Message, "Array length 2147483647 exceeds MaxArrayElements (1000000) (field 'data' (uint8)");
    }

    /// <summary>A short array says how many elements were needed.</summary>
    [TestMethod]
    public void ShortArray_ShowsElementsNeededAndAvailable()
    {
        var layout = new CStruct("struct p { uint32 n; uint8 data[n]; };");

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 5, 0, 0, 0, 1, 2, }, "p"));

        StringAssert.StartsWith(exception.Message, "Not enough bytes: 5 elements of 1 bytes need 5, available 2 (field 'data' (uint8)");
    }

    /// <summary>A terminated string without a terminator says so.</summary>
    [TestMethod]
    public void MissingTerminator_IsExplained()
    {
        var layout = new CStruct("struct s { char name[]; uint8 x; };");

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 65, 66, }, "s"));

        StringAssert.StartsWith(exception.Message, "Not enough bytes: the terminated string has no terminator before the end of the input (field 'name' (cstring)");
    }

    /// <summary>An unknown enum member name surfaces the reason.</summary>
    [TestMethod]
    public void UnknownEnumMember_SurfacesTheReason()
    {
        var layout = new CStruct("enum color : uint8 { red = 1 }; struct s { color c; };");

        CStructWriteException exception = Assert.Throws<CStructWriteException>(() => layout.Serialize("s", new Dictionary<string, object?> { ["c"] = "blue" }));

        StringAssert.StartsWith(exception.Message, "Cannot write the supplied value as enum 'color': 'blue' is neither a member of enum 'color' nor an invariant decimal integer (field 'c' (color)");
    }

    /// <summary>A dangling pointer reports the target address and the field that held it.</summary>
    [TestMethod]
    public void DanglingPointer_ReportsTargetAndField()
    {
        var layout = new CStruct("struct s { uint32 v; uint32 *p; };", pointerSize: 4);

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 1, 0, 0, 0, 100, 0, 0, 0, }, "s"));

        StringAssert.StartsWith(exception.Message, "Pointer target is outside the readable stream range: 100 (field 'p' (uint32)");
    }
}
