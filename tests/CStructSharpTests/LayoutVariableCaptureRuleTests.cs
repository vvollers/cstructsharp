namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The one layout-variable capture rule, checked through every operation that captures: an expression may use only
///     integer fields; a field that is not an integer fails construction when it alone supplies a name, and makes a
///     shared name unusable; an integer outside the Int32 range fails with its exact value when used.
/// </summary>
[TestClass]
public class LayoutVariableCaptureRuleTests
{
    /// <summary>A name only a non-integer field supplies fails layout construction and names what the field holds.</summary>
    /// <param name="declaration">The field declaration.</param>
    /// <param name="reason">What the diagnostic says the field is.</param>
    [TestMethod]
    [DataRow("char name[4];", "text")]
    [DataRow("utf8 name[4];", "text")]
    [DataRow("cstring name;", "text")]
    [DataRow("char name[];", "text")]
    [DataRow("uint8 name[2];", "an array")]
    [DataRow("float32 name;", "a floating-point value")]
    [DataRow("fixed16_16 name;", "a fixed-point value")]
    [DataRow("struct { uint8 a; } name;", "a struct")]
    public void NonIntegerField_InAnExpression_FailsConstruction(string declaration, string reason)
    {
        string layout = "struct root { " + declaration + " uint8 data[name]; };";

        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct(layout));
        StringAssert.Contains(failure.Message, $"Field 'name' is {reason}, but a layout expression uses it");
        Assert.AreEqual(layout.IndexOf("name", StringComparison.Ordinal), failure.SourceOffset);
    }

    /// <summary>A definition of the same name keeps the name valid; the definition's value applies until the field is read.</summary>
    [TestMethod]
    public void NonIntegerField_WithADefinitionOfTheSameName_Constructs()
    {
        var layout = new CStruct("#define name 2\nstruct root { uint8 data[name]; char name[2]; };");
        StructValue value = layout.Parse(new byte[] { 1, 2, 65, 66, }, "root");
        Assert.AreEqual("AB", (string)value["name"]!);
    }

    /// <summary>
    ///     A text field sharing its name with an integer field makes the name unusable while its value is in effect, with
    ///     the same message through reading, selected reading, address resolution, updating and writing.
    /// </summary>
    [TestMethod]
    public void SharedName_TextCapture_FailsEveryOperationAlike()
    {
        var layout = new CStruct("struct inner { char n[2]; }; struct root { uint8 n; inner i; uint8 v[n]; };");
        byte[] bytes = [1, 65, 66, 7,];
        const string expected = "'n' is text, but layout expressions can only use integer fields";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(
            Assert.Throws<Exception>(() => layout.Serialize("root", new Dictionary<string, object?> { ["n"] = 1, ["i"] = new Dictionary<string, object?> { ["n"] = "AB", }, ["v"] = new byte[] { 7, }, })).Message,
            expected);
    }

    /// <summary>A pointer address outside the Int32 range fails with the exact address through every operation.</summary>
    [TestMethod]
    public void WidePointerAddress_FailsEveryOperationWithItsValue()
    {
        var layout = new CStruct("struct root { uint8 *p; uint8 v[p]; };", pointerSize: 8);
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7,];
        var options = new ReadOptions { DereferencePointers = false, };
        const string expected = "'p' is 4294967296, which is outside the 32-bit range";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Serialize("root", new Dictionary<string, object?> { ["p"] = 4294967296L, ["v"] = new byte[] { 7, }, })).Message, expected);
    }

    /// <summary>An enum value outside the Int32 range fails with the exact number through every operation.</summary>
    [TestMethod]
    public void WideEnumValue_FailsEveryOperationWithItsValue()
    {
        var layout = new CStruct("enum big : uint64 { X = 0x100000000 }; struct root { big e; uint8 v[e]; };");
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7,];
        const string expected = "'e' is 4294967296, which is outside the 32-bit range";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Serialize("root", new Dictionary<string, object?> { ["e"] = "X", ["v"] = new byte[] { 7, }, })).Message, expected);
    }

    /// <summary>Integer, character, bool, enum and pointer fields all read as numbers, identically when read and when resolved.</summary>
    [TestMethod]
    public void IntegerFields_ReadTheSameThroughEveryPath()
    {
        var layout = new CStruct(
            "enum kind : uint8 { A = 2 }; struct root { uint8 u; char c; bool b; kind k; uint8 *p; uint8 v[u + b + k + p + (c - 64)]; };",
            pointerSize: 1);
        byte[] bytes = [1, 65, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0,];
        var options = new ReadOptions { DereferencePointers = false, };

        // u + b + k + p + (c - 64) = 1 + 1 + 2 + 3 + 1 = 8 elements after the five one-byte fields.
        StructValue value = layout.Parse(bytes, "root", options: options);
        Assert.HasCount(8, (System.Collections.IList)value["v"]!);
        Assert.AreEqual(5L, layout.ResolveAddress(bytes, "root.v", options: options));
        Assert.AreEqual(8, layout.GetArrayLength(bytes, "root.v", options: options));
    }
}
